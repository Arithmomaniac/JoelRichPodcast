from __future__ import annotations

import sys
import unittest
from pathlib import Path
from unittest.mock import Mock, patch

sys.path.insert(0, str(Path(__file__).resolve().parents[2] / "src" / "TorahDlApi"))

import resolver  # noqa: E402


class ResolverTests(unittest.TestCase):
    def test_reviewed_minchas_asher_mapping_resolves_without_network(self) -> None:
        source_url, expected_media_url = next(iter(resolver.REVIEWED_MEDIA_MAPPINGS.items()))

        with patch.object(resolver, "extract") as extract:
            result = resolver.resolve_url(source_url)

        self.assertEqual(expected_media_url, result["download_url"])
        self.assertEqual("reviewed-mapping", result["resolver"])
        extract.assert_not_called()

    def test_confirmed_non_audio_exclusion_is_exact(self) -> None:
        result = resolver.resolve_url("https://traditiononline.org/reconceiving-conceptualism/")

        self.assertEqual(resolver.REASON_CONFIRMED_NON_AUDIO, result["reason_code"])
        self.assertNotIn("download_url", result)

    def test_choose_media_url_prefers_audio_over_video(self) -> None:
        media_url = resolver.choose_media_url([
            "https://example.test/lecture.mp4",
            "https://example.test/lecture.mp3",
        ])

        self.assertEqual("https://example.test/lecture.mp3", media_url)

    def test_choose_media_url_keeps_download_preference_within_audio(self) -> None:
        media_url = resolver.choose_media_url([
            "https://example.test/preview.mp3",
            "https://example.test/mf/download/lecture.mp3",
            "https://example.test/mf/download/lecture.mp4",
        ])

        self.assertEqual("https://example.test/mf/download/lecture.mp3", media_url)

    def test_choose_media_url_keeps_preference_for_audio_without_player_query(self) -> None:
        media_url = resolver.choose_media_url([
            "https://example.test/lecture.mp3?_=1",
            "https://example.test/lecture.mp3",
        ])

        self.assertEqual("https://example.test/lecture.mp3", media_url)

    @patch.object(resolver.requests, "get")
    def test_spreaker_iframe_resolves_to_stable_publisher_endpoint(self, get: Mock) -> None:
        page_response = Mock(
            headers={"content-type": "text/html"},
            text=(
                '<iframe src="https://widget.spreaker.com/player?episode_id=72546049'
                '&amp;theme=dark"></iframe>'
            ),
        )
        page_response.raise_for_status.return_value = None
        api_response = Mock()
        api_response.raise_for_status.return_value = None
        api_response.json.return_value = {
            "response": {
                "episode": {
                    "title": "Finance Debate",
                    "download_url": "https://api.spreaker.com/v2/episodes/72546049/download.mp3",
                }
            }
        }
        get.side_effect = [page_response, api_response]

        result = resolver.resolve_with_embedded_media("https://18forty.org/podcast/finance-debate/")

        self.assertEqual("https://api.spreaker.com/v2/episodes/72546049/download.mp3", result["download_url"])
        self.assertEqual("Finance Debate", result["title"])

    @patch.object(resolver.requests, "get")
    def test_yutorah_youtube_embed_is_access_blocked_not_fabricated_mp4(self, get: Mock) -> None:
        response = Mock(
            text='<iframe src="//www.youtube.com/embed/_JytEaeRgcw?feature=oembed"></iframe>',
        )
        response.raise_for_status.return_value = None
        get.return_value = response

        with self.assertRaisesRegex(resolver.ResolutionFailure, "_JytEaeRgcw") as context:
            resolver.resolve_yutorah_mp4("https://www.yutorah.org/lectures/1185079")

        self.assertEqual(resolver.REASON_ACCESS_BLOCKED, context.exception.reason_code)

    def test_failure_reason_prioritizes_access_blocked(self) -> None:
        reason_code = resolver.choose_reason_code([
            resolver.REASON_NO_MEDIA_FOUND,
            resolver.REASON_ACCESS_BLOCKED,
            resolver.REASON_RESOLVER_ERROR,
        ])

        self.assertEqual(resolver.REASON_ACCESS_BLOCKED, reason_code)

    def test_direct_youtube_is_not_published_as_transient_enclosure(self) -> None:
        result = resolver.resolve_url("https://www.youtube.com/watch?v=teew7qIflao")

        self.assertEqual(resolver.REASON_ACCESS_BLOCKED, result["reason_code"])
        self.assertNotIn("download_url", result)


if __name__ == "__main__":
    unittest.main()
