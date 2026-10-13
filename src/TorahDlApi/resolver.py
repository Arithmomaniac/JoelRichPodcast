from __future__ import annotations

import html
import logging
import re
from pathlib import PurePosixPath
from urllib.parse import quote, unquote, urlsplit, urlunsplit

import requests
from torah_dl import extract
from yt_dlp import YoutubeDL
from yt_dlp.utils import DownloadError, UnsupportedError

LOGGER = logging.getLogger(__name__)

REQUEST_TIMEOUT_SECONDS = 20
YTDLP_SOCKET_TIMEOUT_SECONDS = 20

MEDIA_URL_PATTERN = re.compile(
    r"https?://[^\"'\s<>]+?\.(?:mp3|m4a|wav|ogg|mp4|webm)(?:\?[^\"'\s<>]*)?",
    re.IGNORECASE,
)
YUTORAH_LECTURE_PATTERN = re.compile(
    r"https?://(?:www\.)?yutorah\.org/lectures/(?:lecture\.cfm/)?(\d+)(?:/|$)",
    re.IGNORECASE,
)
TITLE_PATTERN = re.compile(r"<title>(.*?)</title>", re.IGNORECASE | re.DOTALL)
SPREAKER_PLAYER_PATTERN = re.compile(
    r"""<iframe\b[^>]*\bsrc=["'](?P<src>https://widget\.spreaker\.com/player\?[^"']+)["']""",
    re.IGNORECASE,
)
YOUTUBE_EMBED_PATTERN = re.compile(
    r"""<iframe\b[^>]*\bsrc=["'](?://|https?://)(?:www\.)?youtube\.com/embed/(?P<id>[\w-]+)""",
    re.IGNORECASE,
)

REASON_ACCESS_BLOCKED = "access_blocked"
REASON_CONFIRMED_NON_AUDIO = "confirmed_non_audio"
REASON_NO_MEDIA_FOUND = "no_media_found"
REASON_RESOLVER_ERROR = "resolver_error"
REASON_TIMEOUT = "timeout"
REASON_UNCLASSIFIED = "unclassified"

CONFIRMED_NON_AUDIO_URLS = {
    "https://traditiononline.org/reconceiving-conceptualism/",
}

REVIEWED_MEDIA_MAPPINGS = {
    "https://minchasasher.com/he/shiur/%d7%a4%d7%a8%d7%a9%d7%94-%d7%a9%d7%99%d7%a2%d7%95%d7%a8%d7%99%d7%9d/%d7%95%d7%a0%d7%a9%d7%9e%d7%a8%d7%aa%d7%9d-%d7%9e%d7%90%d7%95%d7%93-%d7%9c%d7%a0%d7%a4%d7%a9%d7%95%d7%aa%d7%99%d7%9b%d7%9d/":
        "https://m.torahbase.org/mp3/Vaeschanan_he_81.mp3",
    "https://minchasasher.com/he/shiur/%d7%a4%d7%a8%d7%a9%d7%94-%d7%a9%d7%99%d7%a2%d7%95%d7%a8%d7%99%d7%9d/%d7%91%d7%a2%d7%a0%d7%99%d7%9f-%d7%aa%d7%95%d7%9c%d7%a2%d7%99%d7%9d-%d7%96%d7%a2%d7%99%d7%a8%d7%99%d7%9d/":
        "https://m.torahbase.org/mp3/Reeh_he_80.mp3",
    "https://minchasasher.com/he/shiur/%d7%a4%d7%a8%d7%a9%d7%94-%d7%a9%d7%99%d7%a2%d7%95%d7%a8%d7%99%d7%9d/%d7%92%d7%93%d7%a8%d7%99-%d7%94%d7%a6%d7%95%d7%9e%d7%95%d7%aa-%d7%91%d7%99%d7%a9%d7%a8%d7%90%d7%9c/":
        "https://m.torahbase.org/mp3/Devarim_he_84.mp3",
    "https://minchasasher.com/he/shiur/%d7%a4%d7%a8%d7%a9%d7%94-%d7%a9%d7%99%d7%a2%d7%95%d7%a8%d7%99%d7%9d/%d7%a9%d7%95%d7%97%d7%93-%d7%95%d7%97%d7%99%d7%95%d7%91-%d7%94%d7%a9%d7%91%d7%aa%d7%95/":
        "https://m.torahbase.org/mp3/Shoftim_he_85.mp3",
}

CONTENT_TYPES_BY_EXTENSION = {
    ".mp3": "audio/mpeg",
    ".m4a": "audio/mp4",
    ".wav": "audio/wav",
    ".ogg": "audio/ogg",
    ".mp4": "video/mp4",
    ".webm": "video/webm",
}


class ResolutionFailure(RuntimeError):
    def __init__(self, reason_code: str, message: str):
        super().__init__(message)
        self.reason_code = reason_code


def resolve_url(url: str) -> dict:
    """Resolve a page URL to direct podcast media using torah-dl first."""
    if url in CONFIRMED_NON_AUDIO_URLS:
        return {
            "url": url,
            "error": "reviewed source is an article/PDF without podcast media",
            "reason_code": REASON_CONFIRMED_NON_AUDIO,
        }

    if media_url := REVIEWED_MEDIA_MAPPINGS.get(url):
        return {"url": url, "resolver": "reviewed-mapping", **build_result(media_url)}

    if is_youtube_url(url):
        return {
            "url": url,
            "error": "YouTube does not expose a durable unauthenticated podcast enclosure",
            "reason_code": REASON_ACCESS_BLOCKED,
        }

    if is_apple_podcast_show_url(url):
        return {
            "url": url,
            "error": "unsupported Apple Podcasts show page",
            "reason_code": REASON_NO_MEDIA_FOUND,
        }

    errors: list[str] = []
    reason_codes: list[str] = []
    resolvers = [("torah-dl", resolve_with_torah_dl)]
    if is_apple_podcast_url(url):
        resolvers.append(("yt-dlp", resolve_with_yt_dlp))
    resolvers.extend((
        ("embedded-media", resolve_with_embedded_media),
        ("yutorah-mp4", resolve_yutorah_mp4),
    ))
    if not any(name == "yt-dlp" for name, _ in resolvers):
        resolvers.append(("yt-dlp", resolve_with_yt_dlp))

    for name, resolver in resolvers:
        try:
            result = resolver(url)
        except ResolutionFailure as e:
            errors.append(f"{name}: {e}")
            reason_codes.append(e.reason_code)
            continue
        except Exception as e:  # Resolver boundary: keep trying the fallback chain.
            errors.append(f"{name}: {e}")
            reason_codes.append(classify_error(e))
            LOGGER.debug("%s resolver failed for %s", name, url, exc_info=e)
            continue

        if result is not None and result.get("download_url"):
            return {"url": url, "resolver": name, **result}

    return {
        "url": url,
        "error": "; ".join(errors) or "no resolver returned a download URL",
        "reason_code": choose_reason_code(reason_codes),
    }


def resolve_with_torah_dl(url: str) -> dict | None:
    result = extract(url)
    if not result.download_url:
        return None

    return {
        "download_url": iri_to_uri(result.download_url),
        "title": result.title,
        "file_format": normalize_content_type(result.file_format) or content_type_from_url(result.download_url),
        "file_name": result.file_name or file_name_from_url(result.download_url),
    }


def resolve_with_embedded_media(url: str) -> dict | None:
    response = requests.get(url, timeout=REQUEST_TIMEOUT_SECONDS, headers={"User-Agent": "Mozilla/5.0"})
    response.raise_for_status()

    content_type = response.headers.get("content-type", "")
    if content_type.startswith(("audio/", "video/")):
        return build_result(response.url)

    if "html" not in content_type and "text" not in content_type:
        return None

    media_urls = sorted({iri_to_uri(html.unescape(match.group(0))) for match in MEDIA_URL_PATTERN.finditer(response.text)})
    if media_urls:
        media_url = choose_media_url(media_urls)
        return build_result(media_url, extract_title(response.text))

    return resolve_spreaker_embed(response.text)


def resolve_yutorah_mp4(url: str) -> dict | None:
    if not (match := YUTORAH_LECTURE_PATTERN.search(url)):
        return None

    shiur_id = match.group(1)
    iframe_url = f"https://classic.yutorah.org/lectures/lecture_iframe.cfm/{shiur_id}"
    response = requests.get(iframe_url, timeout=REQUEST_TIMEOUT_SECONDS, headers={"User-Agent": "torah-dl/1.0"})
    response.raise_for_status()

    media_urls = sorted({iri_to_uri(html.unescape(match.group(0))) for match in MEDIA_URL_PATTERN.finditer(response.text)})
    mp4_urls = [media_url for media_url in media_urls if media_url.lower().split("?", 1)[0].endswith(".mp4")]
    if mp4_urls:
        media_url = next((url for url in mp4_urls if not re.search(r"/\d+\.mp4$", urlsplit(url).path)), mp4_urls[0])
        return build_result(media_url, extract_yutorah_title(response.text))

    if match := YOUTUBE_EMBED_PATTERN.search(response.text):
        raise ResolutionFailure(
            REASON_ACCESS_BLOCKED,
            f"YUTorah lecture embeds YouTube video {match.group('id')} without a durable podcast enclosure",
        )

    return None


def resolve_spreaker_embed(text: str) -> dict | None:
    if not (match := SPREAKER_PLAYER_PATTERN.search(text)):
        return None

    player_url = html.unescape(match.group("src"))
    episode_ids = [
        part.removeprefix("episode_id=")
        for part in urlsplit(player_url).query.split("&")
        if part.startswith("episode_id=")
    ]
    if len(episode_ids) != 1 or not episode_ids[0].isdigit():
        raise ResolutionFailure(REASON_RESOLVER_ERROR, "Spreaker player has no valid episode identifier")

    episode_id = episode_ids[0]
    response = requests.get(
        f"https://api.spreaker.com/v2/episodes/{episode_id}",
        timeout=REQUEST_TIMEOUT_SECONDS,
        headers={"User-Agent": "Mozilla/5.0"},
    )
    response.raise_for_status()
    episode = response.json().get("response", {}).get("episode", {})
    media_url = episode.get("download_url") or episode.get("playback_url")
    if not media_url:
        raise ResolutionFailure(REASON_NO_MEDIA_FOUND, "Spreaker episode has no public media URL")

    return build_result(media_url, episode.get("title"))


def resolve_with_yt_dlp(url: str) -> dict | None:
    options = {
        "format": "bestaudio/best",
        "noplaylist": True,
        "quiet": True,
        "no_warnings": True,
        "skip_download": True,
        "socket_timeout": YTDLP_SOCKET_TIMEOUT_SECONDS,
    }
    try:
        with YoutubeDL(options) as ydl:
            info = ydl.extract_info(url, download=False)
    except (DownloadError, UnsupportedError) as e:
        raise RuntimeError(str(e)) from e

    if not isinstance(info, dict) or info.get("_type") == "playlist":
        return None

    media_url = info.get("url")
    if not media_url:
        return None

    file_format = normalize_content_type(content_type_from_yt_dlp(info)) or content_type_from_url(media_url)
    return {
        "download_url": iri_to_uri(media_url),
        "title": info.get("title"),
        "file_format": file_format,
        "file_name": file_name_from_url(media_url),
    }


def choose_media_url(media_urls: list[str]) -> str:
    audio_urls = [
        url for url in media_urls
        if urlsplit(url).path.lower().endswith((".mp3", ".m4a", ".wav", ".ogg"))
    ]
    candidates = audio_urls or media_urls
    return (
        next((url for url in candidates if "/mf/download/" in url), None)
        or next((url for url in candidates if "?_=" not in url), None)
        or candidates[0]
    )


def classify_error(error: Exception) -> str:
    message = str(error).lower()
    if "timed out" in message or "timeout" in message:
        return REASON_TIMEOUT
    if any(signal in message for signal in ("403", "forbidden", "not a bot", "sign in to confirm", "cloudflare")):
        return REASON_ACCESS_BLOCKED
    if "unsupported url" in message:
        return REASON_NO_MEDIA_FOUND
    return REASON_RESOLVER_ERROR


def choose_reason_code(reason_codes: list[str]) -> str:
    for reason_code in (REASON_ACCESS_BLOCKED, REASON_TIMEOUT, REASON_RESOLVER_ERROR, REASON_NO_MEDIA_FOUND):
        if reason_code in reason_codes:
            return reason_code
    return REASON_NO_MEDIA_FOUND


def build_result(media_url: str, title: str | None = None) -> dict:
    return {
        "download_url": media_url,
        "title": title,
        "file_format": content_type_from_url(media_url),
        "file_name": file_name_from_url(media_url),
    }


def content_type_from_yt_dlp(info: dict) -> str | None:
    ext = info.get("ext")
    if not ext:
        return None

    normalized_ext = f".{ext.lower().lstrip('.')}"
    if info.get("vcodec") and info.get("vcodec") != "none":
        return f"video/{ext}"
    if info.get("acodec") and info.get("acodec") != "none":
        return f"audio/{ext}"
    if normalized_ext in CONTENT_TYPES_BY_EXTENSION:
        return CONTENT_TYPES_BY_EXTENSION[normalized_ext]
    return None


def normalize_content_type(content_type: str | None) -> str | None:
    if content_type == "audio/mp3":
        return "audio/mpeg"
    return content_type


def content_type_from_url(url: str) -> str:
    ext = PurePosixPath(urlsplit(url).path).suffix.lower()
    return CONTENT_TYPES_BY_EXTENSION.get(ext, "audio/mpeg")


def file_name_from_url(url: str) -> str | None:
    name = PurePosixPath(urlsplit(url).path).name
    if not name:
        return None

    decoded_name = unquote(name)
    if decoded_name.startswith(("http://", "https://")):
        return file_name_from_url(decoded_name)
    return decoded_name


def extract_title(text: str) -> str | None:
    if not (match := TITLE_PATTERN.search(text)):
        return None

    title = re.sub(r"\s+", " ", html.unescape(match.group(1))).strip()
    return title or None


def extract_yutorah_title(text: str) -> str | None:
    title = extract_title(text)
    if title and title.startswith("YUTorah Online - "):
        title = title.replace("YUTorah Online - ", "", 1)
        title = re.sub(r"\s+\(Rabbi.*\)$", "", title).strip()
    return title


def is_apple_podcast_url(url: str) -> bool:
    return urlsplit(url).netloc.lower() == "podcasts.apple.com"


def is_apple_podcast_show_url(url: str) -> bool:
    parts = urlsplit(url)
    if parts.netloc.lower() != "podcasts.apple.com":
        return False

    if "/id" not in parts.path.lower():
        return False

    return not any(part.lower().startswith("i=") for part in parts.query.split("&") if part)


def is_youtube_url(url: str) -> bool:
    host = urlsplit(url).netloc.lower()
    return host in {"youtube.com", "www.youtube.com", "youtu.be", "m.youtube.com"}


def iri_to_uri(url: str) -> str:
    parts = urlsplit(html.unescape(url))
    return urlunsplit((
        parts.scheme,
        parts.netloc.encode("idna").decode("ascii"),
        quote(unquote(parts.path), safe="/%:@"),
        quote(unquote(parts.query), safe="=&?/%:@,+"),
        quote(unquote(parts.fragment), safe="=&?/%:@,+"),
    ))
