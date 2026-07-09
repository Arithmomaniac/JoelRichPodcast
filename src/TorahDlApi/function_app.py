import json
import logging
from concurrent.futures import ThreadPoolExecutor, TimeoutError as FuturesTimeoutError

import azure.functions as func
from resolver import resolve_url

app = func.FunctionApp(http_auth_level=func.AuthLevel.ANONYMOUS)


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

    PER_URL_TIMEOUT_SECONDS = 30

    def resolve_one(url: str) -> dict:
        result = resolve_url(url)
        if result.get("download_url") is None:
            logging.warning("URL resolution failed for %s: %s", url, result.get("error"))
        return result

    results = []
    # Process each URL with a per-URL timeout so one hang can't block the batch
    with ThreadPoolExecutor(max_workers=1) as executor:
        for url in urls:
            future = executor.submit(resolve_one, url)
            try:
                results.append(future.result(timeout=PER_URL_TIMEOUT_SECONDS))
            except FuturesTimeoutError:
                logging.warning("torah-dl timed out for %s", url)
                results.append({"url": url, "error": "timeout"})

    return func.HttpResponse(
        json.dumps(results),
        mimetype="application/json",
    )
