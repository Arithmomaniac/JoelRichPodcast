from __future__ import annotations

import json
import os
import signal
import sys
import time
import unittest
from pathlib import Path
from unittest.mock import patch

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "src" / "TorahDlApi"))

import azure.functions as func  # noqa: E402
import function_app  # noqa: E402
from function_app import resolve_many_with_timeout  # noqa: E402
from resolver import REVIEWED_MEDIA_MAPPINGS  # noqa: E402


def mixed_resolver(url: str) -> dict:
    if url.endswith("/slow"):
        time.sleep(10)
        return {"url": url, "download_url": "https://media.example.test/late.mp3"}
    return {"url": url, "download_url": "https://media.example.test/fast.mp3"}


def stubborn_resolver(url: str) -> dict:
    signal.signal(signal.SIGTERM, signal.SIG_IGN)
    time.sleep(10)
    return {"url": url, "download_url": "https://media.example.test/late.mp3"}


class ResolveTimeoutTests(unittest.TestCase):
    def test_timed_out_process_does_not_delay_following_resolution(self) -> None:
        started = time.monotonic()

        timed_out, resolved = resolve_many_with_timeout(
            ["https://example.test/slow", "https://example.test/fast"],
            3,
            mixed_resolver,
        )

        self.assertEqual("timeout", timed_out["reason_code"])
        self.assertEqual("https://media.example.test/fast.mp3", resolved["download_url"])
        self.assertLess(time.monotonic() - started, 7)

    @unittest.skipIf(os.name == "nt", "Windows TerminateProcess cannot be ignored.")
    def test_timeout_kills_worker_that_ignores_sigterm(self) -> None:
        started = time.monotonic()

        result = resolve_many_with_timeout(["https://example.test/stubborn"], 3, stubborn_resolver)[0]

        self.assertEqual("timeout", result["reason_code"])
        self.assertLess(time.monotonic() - started, 7)

    def test_http_resolution_returns_reviewed_media_and_classified_failures(self) -> None:
        source_url, media_url = next(iter(REVIEWED_MEDIA_MAPPINGS.items()))
        urls = [
            source_url,
            "https://www.youtube.com/watch?v=teew7qIflao",
            "https://traditiononline.org/reconceiving-conceptualism/",
        ]
        request = func.HttpRequest(
            method="POST",
            url="https://resolver.test/api/resolve",
            body=json.dumps(urls).encode(),
        )

        response = function_app.resolve.build().get_user_function()(request)
        results = json.loads(response.get_body())

        self.assertEqual(200, response.status_code)
        self.assertEqual(urls, [result["url"] for result in results])
        self.assertEqual(media_url, results[0]["download_url"])
        self.assertEqual("access_blocked", results[1]["reason_code"])
        self.assertEqual("confirmed_non_audio", results[2]["reason_code"])

    def test_http_resolution_rejects_invalid_items_before_starting_workers(self) -> None:
        request = func.HttpRequest(
            method="POST",
            url="https://resolver.test/api/resolve",
            body=b'["https://example.test", null]',
        )
        with patch.object(function_app, "resolve_many_with_timeout") as resolve_many:
            response = function_app.resolve.build().get_user_function()(request)

        self.assertEqual(400, response.status_code)
        resolve_many.assert_not_called()


if __name__ == "__main__":
    unittest.main()
