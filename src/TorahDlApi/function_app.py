import json
import logging
import multiprocessing
import time
from collections.abc import Callable
from concurrent.futures import ThreadPoolExecutor
from multiprocessing.connection import Connection
from multiprocessing.process import BaseProcess

import azure.functions as func
from resolver import (
    REASON_RESOLVER_ERROR,
    REASON_TIMEOUT,
    REASON_UNCLASSIFIED,
    choose_reason_code,
    resolve_url,
    resolve_without_network,
)

app = func.FunctionApp(http_auth_level=func.AuthLevel.ANONYMOUS)
PER_URL_TIMEOUT_SECONDS = 30
WORKER_STARTUP_TIMEOUT_SECONDS = 30
# Leave room for host startup and serialization before Azure's 230-second HTTP limit.
BATCH_TIMEOUT_SECONDS = 200


def _stop_process(process: BaseProcess) -> None:
    process.terminate()
    process.join(timeout=1)
    if process.is_alive():
        process.kill()
        process.join(timeout=1)
    if process.is_alive():
        raise RuntimeError(f"Resolver worker {process.pid} did not stop after being killed")


def _resolve_in_process(
    resolver: Callable[[str], dict],
    url: str,
    send_connection: Connection,
) -> None:
    try:
        send_connection.send(("ready", None))
        if resolver is resolve_url:
            result = resolve_url(url, report_progress=lambda progress: send_connection.send(("progress", progress)))
        else:
            result = resolver(url)
        send_connection.send(("result", result))
    except Exception as error:
        send_connection.send(("result", {
            "url": url,
            "error": f"resolver process failed: {error}",
            "reason_code": REASON_RESOLVER_ERROR,
        }))
    finally:
        send_connection.close()


def resolve_with_timeout(
    url: str,
    timeout_seconds: float = PER_URL_TIMEOUT_SECONDS,
    resolver: Callable[[str], dict] = resolve_url,
    startup_timeout_seconds: float = WORKER_STARTUP_TIMEOUT_SECONDS,
    batch_deadline: float | None = None,
) -> dict:
    if resolver is resolve_url and (result := resolve_without_network(url)):
        return result

    if batch_deadline is not None and time.monotonic() >= batch_deadline:
        return {
            "url": url,
            "error": "Batch deadline reached before the resolver worker could start",
            "reason_code": REASON_RESOLVER_ERROR,
        }

    context = multiprocessing.get_context("spawn")
    receive_connection, send_connection = context.Pipe(duplex=False)
    process = context.Process(
        target=_resolve_in_process,
        args=(resolver, url, send_connection),
        daemon=True,
    )
    phase = "worker startup"
    stage = phase
    errors: list[str] = []
    reason_codes: list[str] = []
    deadline = time.monotonic() + startup_timeout_seconds
    if batch_deadline is not None:
        deadline = min(deadline, batch_deadline)
    try:
        process.start()
        send_connection.close()
        while receive_connection.poll(max(0, deadline - time.monotonic())):
            try:
                message_type, payload = receive_connection.recv()
            except EOFError:
                errors.append("resolver process exited without returning a result")
                return {
                    "url": url,
                    "error": "; ".join(errors),
                    "reason_code": choose_reason_code([*reason_codes, REASON_RESOLVER_ERROR]),
                }
            if message_type == "ready":
                phase = "extraction"
                stage = phase
                deadline = time.monotonic() + timeout_seconds
                if batch_deadline is not None:
                    deadline = min(deadline, batch_deadline)
            elif message_type == "progress":
                stage = payload["stage"]
                if payload.get("error"):
                    errors.append(payload["error"])
                    reason_codes.append(payload["reason_code"])
            elif message_type == "result":
                process.join(timeout=1)
                return payload

        budget = "Batch deadline reached" if batch_deadline is not None and time.monotonic() >= batch_deadline else (
            f"{phase.capitalize()} exceeded {startup_timeout_seconds if phase == 'worker startup' else timeout_seconds:g}s"
        )
        errors.append(f"{budget} during {stage}")
        reason_code = (
            REASON_RESOLVER_ERROR if phase == "worker startup"
            else choose_reason_code([*reason_codes, REASON_TIMEOUT])
        )
        logging.warning("Resolver deadline for %s: %s", url, errors[-1])
        return {"url": url, "error": "; ".join(errors), "reason_code": reason_code}
    finally:
        if process.is_alive():
            _stop_process(process)
        send_connection.close()
        receive_connection.close()


def resolve_many_with_timeout(
    urls: list[str],
    timeout_seconds: float = PER_URL_TIMEOUT_SECONDS,
    resolver: Callable[[str], dict] = resolve_url,
    batch_timeout_seconds: float = BATCH_TIMEOUT_SECONDS,
) -> list[dict]:
    if not urls:
        return []

    batch_deadline = time.monotonic() + batch_timeout_seconds
    with ThreadPoolExecutor(max_workers=min(4, len(urls))) as executor:
        return list(executor.map(
            lambda url: resolve_with_timeout(url, timeout_seconds, resolver, batch_deadline=batch_deadline),
            urls,
        ))


@app.route(route="resolve", methods=["POST"])
def resolve(req: func.HttpRequest) -> func.HttpResponse:
    """Resolve one or more URLs to direct audio download links via torah-dl.

    Request body: JSON array of URL strings, e.g. ["https://yutorah.org/...", ...]
    Response: JSON array of result objects (same order), e.g.
      [
        {"url": "...", "download_url": "...", "title": "...", "file_format": "...", "file_name": "..."},
        {"url": "...", "error": "..."},
        ...
      ]
    """
    try:
        urls = req.get_json()
    except ValueError:
        return func.HttpResponse(
            json.dumps({"error": "Request body must be a JSON array of URLs"}),
            status_code=400,
            mimetype="application/json",
        )

    if not isinstance(urls, list):
        return func.HttpResponse(
            json.dumps({"error": "Request body must be a JSON array of URLs"}),
            status_code=400,
            mimetype="application/json",
        )

    if not all(isinstance(url, str) and url for url in urls):
        return func.HttpResponse(
            json.dumps({"error": "Every URL must be a non-empty string"}),
            status_code=400,
            mimetype="application/json",
        )

    results = resolve_many_with_timeout(urls)
    for url, result in zip(urls, results):
        if result.get("download_url") is None:
            logging.warning(
                "URL resolution failed for %s [%s]: %s",
                url,
                result.get("reason_code", REASON_UNCLASSIFIED),
                result.get("error"),
            )

    return func.HttpResponse(
        json.dumps(results),
        mimetype="application/json",
    )
