using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using JoelRichPodcast.Functions.Models;
using Microsoft.Extensions.Logging;

namespace JoelRichPodcast.Functions.Services;

public partial class TorahDlResolver(
    IHttpClientFactory httpClientFactory,
    ILogger<TorahDlResolver> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    // torah-dl doesn't recognize the legacy lecture.cfm URL format used by Torah Musings
    private static readonly Regex YutorahLectureCfmPattern = new(
        @"(https?://(?:www\.)?yutorah\.org)/lectures/lecture\.cfm/(\d+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// Sites that are recognized but require authentication or are otherwise unsupported
    /// by torah-dl. URLs matching these patterns are skipped silently (Debug-level log)
    /// rather than producing Warning-level "Could not resolve" messages.
    /// </summary>
    [GeneratedRegex(@"https?://(?:www\.)?torahinmotion\.org/", RegexOptions.IgnoreCase)]
    private static partial Regex UnsupportedSitePattern();

    /// <summary>
    /// Resolves multiple URLs to direct audio download URLs in a single batch HTTP call.
    /// Direct audio links are resolved locally (fast path); the rest are sent to the torah-dl API.
    /// Returns a dictionary keyed by original URL.
    /// </summary>
    public async Task<Dictionary<string, TorahDlResult?>> ResolveBatchAsync(IReadOnlyList<string> urls)
    {
        var results = new Dictionary<string, TorahDlResult?>(urls.Count);
        var apiUrls = new List<(string original, string normalized)>();

        foreach (var url in urls)
        {
            // Skip known-unsupported sites silently (no Warning, no alert noise)
            if (UnsupportedSitePattern().IsMatch(url))
            {
                logger.LogDebug("Skipping unsupported site: {Url}", url);
                results[url] = null;
                continue;
            }

            var normalized = NormalizeUrl(url);

            if (IsDirectAudioLink(normalized))
            {
                var ext = Path.GetExtension(new Uri(normalized).AbsolutePath).TrimStart('.');
                var contentType = ext switch
                {
                    "mp3" => "audio/mpeg",
                    "m4a" => "audio/mp4",
                    "wav" => "audio/wav",
                    "ogg" => "audio/ogg",
                    _ => "audio/mpeg"
                };
                results[url] = new TorahDlResult(
                    normalized, null, contentType, Path.GetFileName(new Uri(normalized).AbsolutePath));
            }
            else
            {
                apiUrls.Add((url, normalized));
            }
        }

        if (apiUrls.Count == 0)
            return results;

        try
        {
            var client = httpClientFactory.CreateClient("TorahDlApi");
            var payload = apiUrls.Select(u => u.normalized).ToList();
            var response = await client.PostAsJsonAsync("api/resolve", payload);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogError("torah-dl API returned {Status}", response.StatusCode);
                foreach (var (original, _) in apiUrls)
                    results[original] = null;
                return results;
            }

            var apiResults = await response.Content.ReadFromJsonAsync<List<ApiResolveResult>>(JsonOptions);
            if (apiResults is null)
            {
                logger.LogError("torah-dl API returned null response body");
                foreach (var (original, _) in apiUrls)
                    results[original] = null;
                return results;
            }

            if (apiResults.Count != apiUrls.Count)
            {
                logger.LogWarning("torah-dl API returned {Count} results for {Expected} URLs — processing available results",
                    apiResults.Count, apiUrls.Count);
            }

            // Match results by URL field rather than position, so partial responses still work
            var resultsByUrl = new Dictionary<string, ApiResolveResult>(StringComparer.OrdinalIgnoreCase);
            foreach (var r in apiResults)
            {
                if (r.Url is not null)
                    resultsByUrl[r.Url] = r;
            }

            foreach (var (original, normalized) in apiUrls)
            {
                if (!resultsByUrl.TryGetValue(normalized, out var apiResult))
                {
                    logger.LogWarning("No result returned for {Url}", original);
                    results[original] = null;
                }
                else if (apiResult.Error is not null)
                {
                    logger.LogWarning("Could not resolve: {Url} — {Error}", original, apiResult.Error);
                    results[original] = null;
                }
                else if (apiResult.DownloadUrl is null)
                {
                    logger.LogWarning("torah-dl returned no download URL for {Url}", original);
                    results[original] = null;
                }
                else
                {
                    results[original] = new TorahDlResult(
                        apiResult.DownloadUrl, apiResult.Title, apiResult.FileFormat, apiResult.FileName);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "torah-dl API batch resolution failed");
            foreach (var (original, _) in apiUrls)
                results.TryAdd(original, null);
        }

        return results;
    }

    /// <summary>
    /// Returns true if the URL belongs to a known-but-unsupported site (e.g. requires login).
    /// Used by the pipeline to decide log level when resolution returns null.
    /// </summary>
    public static bool IsUnsupportedSite(string url) => UnsupportedSitePattern().IsMatch(url);

    /// <summary>
    /// Normalizes URLs to formats that torah-dl can handle.
    /// YUTorah lecture.cfm/ID → /lectures/ID
    /// </summary>
    internal static string NormalizeUrl(string url) =>
        YutorahLectureCfmPattern.Replace(url, "$1/lectures/$2");

    private static bool IsDirectAudioLink(string url)
    {
        try
        {
            var path = new Uri(url).AbsolutePath.ToLowerInvariant();
            return path.EndsWith(".mp3") || path.EndsWith(".m4a")
                || path.EndsWith(".wav") || path.EndsWith(".ogg");
        }
        catch
        {
            return false;
        }
    }

    private record ApiResolveResult(
        string? Url,
        string? DownloadUrl,
        string? Title,
        string? FileFormat,
        string? FileName,
        string? Error);
}
