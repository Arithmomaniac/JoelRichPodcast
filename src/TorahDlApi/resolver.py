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

CONTENT_TYPES_BY_EXTENSION = {
    ".mp3": "audio/mpeg",
    ".m4a": "audio/mp4",
    ".wav": "audio/wav",
    ".ogg": "audio/ogg",
    ".mp4": "video/mp4",
    ".webm": "video/webm",
}


def resolve_url(url: str) -> dict:
    """Resolve a page URL to direct podcast media using torah-dl first."""
    if is_apple_podcast_show_url(url):
        return {"url": url, "error": "unsupported Apple Podcasts show page"}

    errors: list[str] = []
    resolvers = [("torah-dl", resolve_with_torah_dl)]
    if is_apple_podcast_url(url) or is_youtube_url(url):
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
        except Exception as e:  # Resolver boundary: keep trying the fallback chain.
            errors.append(f"{name}: {e}")
            LOGGER.debug("%s resolver failed for %s", name, url, exc_info=e)
            continue

        if result is not None and result.get("download_url"):
            return {"url": url, "resolver": name, **result}

    return {"url": url, "error": "; ".join(errors) or "no resolver returned a download URL"}


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
    if not media_urls:
        return None

    media_url = choose_media_url(media_urls)
    return build_result(media_url, extract_title(response.text))


def resolve_yutorah_mp4(url: str) -> dict | None:
    if not (match := YUTORAH_LECTURE_PATTERN.search(url)):
        return None

    shiur_id = match.group(1)
    iframe_url = f"https://classic.yutorah.org/lectures/lecture_iframe.cfm/{shiur_id}"
    response = requests.get(iframe_url, timeout=REQUEST_TIMEOUT_SECONDS, headers={"User-Agent": "torah-dl/1.0"})
    response.raise_for_status()

    media_urls = sorted({iri_to_uri(html.unescape(match.group(0))) for match in MEDIA_URL_PATTERN.finditer(response.text)})
    mp4_urls = [media_url for media_url in media_urls if media_url.lower().split("?", 1)[0].endswith(".mp4")]
    if not mp4_urls:
        return None

    media_url = next((url for url in mp4_urls if not re.search(r"/\d+\.mp4$", urlsplit(url).path)), mp4_urls[0])
    return build_result(media_url, extract_yutorah_title(response.text))


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
    return (
        next((url for url in media_urls if "/mf/download/" in url), None)
        or next((url for url in media_urls if "?_=" not in url), None)
        or media_urls[0]
    )


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
