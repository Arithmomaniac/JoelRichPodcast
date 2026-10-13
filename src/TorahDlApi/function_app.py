import json
import logging
import multiprocessing
from collections.abc import Callable
from concurrent.futures import ThreadPoolExecutor
from multiprocessing.connection import Connection
from multiprocessing.process import BaseProcess

import azure.functions as func
from resolver import REASON_RESOLVER_ERROR, REASON_TIMEOUT, REASON_UNCLASSIFIED, resolve_url

app = func.FunctionApp(http_auth_level=func.AuthLevel.ANONYMOUS)
PER_URL_TIMEOUT_SECONDS = 30


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
        send_connection.send(resolver(url))
    except Exception as error:
        send_connection.send({
            "url": url,
            "error": f"resolver process failed: {error}",
            "reason_code": REASON_RESOLVER_ERROR,
        })
    finally:
        send_connection.close()


def resolve_with_timeout(
    url: str,
    timeout_seconds: float = PER_URL_TIMEOUT_SECONDS,
    resolver: Callable[[str], dict] = resolve_url,
) -> dict:
    context = multiprocessing.get_context("spawn")
    receive_connection, send_connection = context.Pipe(duplex=False)
    process = context.Process(
        target=_resolve_in_process,
        args=(resolver, url, send_connection),
        daemon=True,
    )
    process.start()
    send_connection.close()

    try:
        if receive_connection.poll(timeout_seconds):
            try:
                result = receive_connection.recv()
            except EOFError:
                result = {
                    "url": url,
                    "error": "resolver process exited without returning a result",
                    "reason_code": REASON_RESOLVER_ERROR,
                }
            process.join(timeout=1)
            if process.is_alive():
                _stop_process(process)
            return result

        _stop_process(process)
        logging.warning("torah-dl timed out for %s", url)
        return {"url": url, "error": "timeout", "reason_code": REASON_TIMEOUT}
    finally:
        receive_connection.close()


def resolve_many_with_timeout(
    urls: list[str],
    timeout_seconds: float = PER_URL_TIMEOUT_SECONDS,
    resolver: Callable[[str], dict] = resolve_url,
) -> list[dict]:
    if not urls:
        return []

    with ThreadPoolExecutor(max_workers=min(4, len(urls))) as executor:
        return list(executor.map(
            lambda url: resolve_with_timeout(url, timeout_seconds, resolver),
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
