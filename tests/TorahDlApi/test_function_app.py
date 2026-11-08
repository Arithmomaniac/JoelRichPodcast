from __future__ import annotations

import json
import os
import signal
import sys
import time
import unittest
from pathlib import Path
from unittest.mock import Mock, patch

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "src" / "TorahDlApi"))

import azure.functions as func  # noqa: E402
import function_app  # noqa: E402
from function_app import resolve_many_with_timeout  # noqa: E402
from resolver import (  # noqa: E402
    REASON_ACCESS_BLOCKED,
    REASON_CONFIRMED_NON_AUDIO,
    REASON_RESOLVER_ERROR,
    REASON_TIMEOUT,
    REVIEWED_MEDIA_MAPPINGS,
)


def mixed_resolver(url: str) -> dict:
    if url.endswith("/slow"):
        time.sleep(10)
        return {"url": url, "download_url": "https://media.example.test/late.mp3"}
    return {"url": url, "download_url": "https://media.example.test/fast.mp3"}


def stubborn_resolver(url: str) -> dict:
    signal.signal(signal.SIGTERM, signal.SIG_IGN)
    time.sleep(10)
    return {"url": url, "download_url": "https://media.example.test/late.mp3"}


def load_delayed_resolver(delay: float):
    time.sleep(delay)
    return mixed_resolver


class DelayedStartupResolver:
    def __reduce__(self):
        return load_delayed_resolver, (0.5,)

    def __call__(self, url: str) -> dict:
        return mixed_resolver(url)


def create_worker_context(messages: list[tuple], *, eof: bool = False) -> Mock:
    receive = Mock()
    receive.poll.side_effect = [True] * (len(messages) + int(eof)) + [False]
    receive.recv.side_effect = [*messages, EOFError()] if eof else messages
    process = Mock()
    process.is_alive.return_value = False
    context = Mock()
    context.Pipe.return_value = receive, Mock()
    context.Process.return_value = process
    return context


class ResolveTimeoutTests(unittest.TestCase):
    def test_worker_startup_does_not_consume_extraction_deadline(self) -> None:
        result = function_app.resolve_with_timeout(
            "https://example.test/fast", timeout_seconds=0.1, resolver=DelayedStartupResolver(),
        )

        self.assertEqual(mixed_resolver(result["url"]), result)

    def test_worker_startup_timeout_is_not_a_publisher_timeout(self) -> None:
        result = function_app.resolve_with_timeout(
            "https://example.test/fast", resolver=DelayedStartupResolver(), startup_timeout_seconds=0.1,
        )

        self.assertEqual(REASON_RESOLVER_ERROR, result["reason_code"])

    def test_known_access_block_survives_later_extraction_timeout(self) -> None:
        context = create_worker_context([
            ("ready", None),
            ("progress", {"stage": "embedded-media", "error": "HTTP 403", "reason_code": REASON_ACCESS_BLOCKED}),
            ("progress", {"stage": "yt-dlp"}),
        ])

        with patch.object(function_app.multiprocessing, "get_context", return_value=context):
            result = function_app.resolve_with_timeout("https://example.test/blocked")

        self.assertEqual(REASON_ACCESS_BLOCKED, result["reason_code"])

    def test_observed_failure_survives_worker_exit(self) -> None:
        for reason_code in (REASON_ACCESS_BLOCKED, REASON_TIMEOUT):
            with self.subTest(reason_code=reason_code):
                failure = {"stage": "embedded-media", "error": "Earlier extraction failure", "reason_code": reason_code}
                context = create_worker_context([("ready", None), ("progress", failure)], eof=True)

                with patch.object(function_app.multiprocessing, "get_context", return_value=context):
                    result = function_app.resolve_with_timeout("https://example.test/failed")

                self.assertEqual(reason_code, result["reason_code"])
                self.assertIn(failure["error"], result["error"])

    def test_batch_deadline_returns_results_for_running_and_queued_urls(self) -> None:
        urls = [f"https://example.test/{index}/slow" for index in range(5)]
        started = time.monotonic()

        results = resolve_many_with_timeout(
            urls, timeout_seconds=10, resolver=mixed_resolver, batch_timeout_seconds=2,
        )

        self.assertEqual(urls, [result["url"] for result in results])
        self.assertTrue(all(result["reason_code"] in {REASON_TIMEOUT, REASON_RESOLVER_ERROR} for result in results))
        self.assertLess(time.monotonic() - started, 5)

    def test_timed_out_process_does_not_delay_following_resolution(self) -> None:
        started = time.monotonic()

        timed_out, resolved = resolve_many_with_timeout(
            ["https://example.test/slow", "https://example.test/fast"],
            3,
            mixed_resolver,
        )

        self.assertEqual(REASON_TIMEOUT, timed_out["reason_code"])
        self.assertEqual("https://media.example.test/fast.mp3", resolved["download_url"])
        self.assertLess(time.monotonic() - started, 7)

    @unittest.skipIf(os.name == "nt", "Windows TerminateProcess cannot be ignored.")
    def test_timeout_kills_worker_that_ignores_sigterm(self) -> None:
        started = time.monotonic()

        result = resolve_many_with_timeout(["https://example.test/stubborn"], 3, stubborn_resolver)[0]

        self.assertEqual(REASON_TIMEOUT, result["reason_code"])
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

        with patch.object(function_app.multiprocessing, "get_context") as get_context:
            response = function_app.resolve.build().get_user_function()(request)
        results = json.loads(response.get_body())

        self.assertEqual(200, response.status_code)
        self.assertEqual(urls, [result["url"] for result in results])
        self.assertEqual(media_url, results[0]["download_url"])
        self.assertEqual(REASON_ACCESS_BLOCKED, results[1]["reason_code"])
        self.assertEqual(REASON_CONFIRMED_NON_AUDIO, results[2]["reason_code"])
        get_context.assert_not_called()

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
