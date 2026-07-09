from __future__ import annotations

import json
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "src" / "TorahDlApi"))

from resolver import resolve_url  # noqa: E402


SAMPLES = {
    "torah-dl-fixed": [
        "https://www.yutorah.org/lectures/lecture.cfm/1177627",
        "https://torahanytime.com/lectures/447734",
    ],
    "fallback-audio": [
        "https://podcasts.apple.com/us/podcast/better-an-apikores-than-an-am-haaretz-rav-meni/id1289716034?i=1000769405161",
        "https://traditiononline.org/podcast-fear-and-faith-in-religious-life/",
        "https://www.podbean.com/media/share/pb-h8t63-1aa5c80?download=1",
    ],
    "fallback-video": [
        "https://www.yutorah.org/lectures/lecture.cfm/1176341",
    ],
    "unsupported": [
        "https://podcasts.apple.com/us/podcast/orthodox-conundrum/id1289716034",
    ],
}


def main() -> int:
    failures: list[dict] = []
    for category, urls in SAMPLES.items():
        for url in urls:
            result = resolve_url(url)
            print(json.dumps({"category": category, **result}, ensure_ascii=True))
            should_resolve = category != "unsupported"
            if should_resolve and not result.get("download_url"):
                failures.append(result)
            if not should_resolve and result.get("download_url"):
                failures.append(result)

    return 1 if failures else 0


if __name__ == "__main__":
    raise SystemExit(main())
