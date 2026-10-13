# Resolution failure operations

The resolver returns its existing success or error fields plus an optional stable `reason_code`.

| Code | Meaning | Alert behavior |
| --- | --- | --- |
| `access_blocked` | The publisher or upstream service requires access that the unattended Azure function does not have. | Alert on the first newly failing URL. |
| `timeout` | Resolution exceeded the per-URL process deadline and the worker process was terminated. | Alert on the first newly failing URL. |
| `confirmed_non_audio` | A specifically reviewed source is not an audio/video episode. | Log as skipped; exclude from the resolution-failure alert. |
| `no_media_found` | The source was reachable but no supported public media was found. | Alert on the first newly failing URL. |
| `resolver_error` | Resolution failed internally or returned malformed provider data. | Alert on the first newly failing URL. |
| `unclassified` | A legacy or missing result did not provide a code. | Alert on the first newly failing URL. |

The .NET pipeline owns the canonical `Could not resolve:` warning. Lower-level Python and C# resolver diagnostics must not use that prefix, because the daily alert deduplicates canonical events by source URL.

## Reviewed exceptions

The four Minchas Asher mappings in `src/TorahDlApi/resolver.py` are exact source-to-publisher-MP3 mappings discovered from the corresponding public pages. They do not suppress or infer media for other Minchas Asher URLs.

`https://traditiononline.org/reconceiving-conceptualism/` is the only confirmed non-audio exclusion. Other Tradition podcast pages continue through normal resolution.

## YouTube disposition

Direct YouTube pages and YouTube-only YUTorah embeds are reported as `access_blocked`. The resolver does not publish yt-dlp's transient Google media URLs as podcast enclosures.

The September 2026 failure set is:

- `teew7qIflao`
- `9pXUomalFOY`
- `_JytEaeRgcw`, embedded by YUTorah lecture `1185079`

Current yt-dlp guidance says YouTube bot challenges may require changing the egress IP, cookies, and PO tokens. Cookies may need frequent rotation, PO tokens are often bound to visitor/session context, and the extracted media request may need the same IP and headers. That is not a durable unauthenticated enclosure that arbitrary podcast clients can fetch later.

Do not add browser-cookie extraction, committed cookies/tokens, proxy rotation, or rehosting without a separate design and explicit approval. Reconsider these entries only if the publisher exposes a stable public audio enclosure or the service architecture is intentionally expanded.

Primary references:

- <https://github.com/yt-dlp/yt-dlp/wiki/FAQ#how-do-i-pass-cookies-to-yt-dlp>
- <https://github.com/yt-dlp/yt-dlp/wiki/Extractors#exporting-youtube-cookies>
- <https://github.com/yt-dlp/yt-dlp/wiki/PO-Token-Guide>

## Historical backfill

`scripts/resolution-backfill.json` contains the five recoverable historical candidates. It is intentionally marked `reviewed: false` until every title, description, roundup index, row key, and media URL has been checked.

The importer uses managed Azure credentials, reads every stored episode, reconciles by `SourceUrl`, and blocks duplicate sources or row-key collisions. It is dry-run-only unless every entry is reviewed and the exact manifest hash is confirmed.

```powershell
dotnet run --project src/JoelRichPodcast.Backfill

dotnet run --project src/JoelRichPodcast.Backfill -- `
  --apply `
  --confirm-manifest-sha256 <hash-from-dry-run>
```

The apply command adds only missing rows with `AddEntityAsync`, then regenerates the feed through the existing `PodcastFeedGenerator`. Re-running the same manifest reports the imported sources as existing and adds nothing.

Required Azure roles are Storage Table Data Reader for dry-run, and Storage Table Data Contributor plus Storage Blob Data Contributor for apply. Do not retrieve storage account keys to bypass missing role assignments.

## Validation

Offline tests:

```powershell
.venv\Scripts\python.exe -m unittest discover -s tests\TorahDlApi -p 'test_*.py'
dotnet test JoelRichPodcast.slnx
```

The live publisher check is intentionally separate because external sites can block or change independently of the code:

```powershell
.venv\Scripts\python.exe scripts\validate_torahdl_resolver.py
```
