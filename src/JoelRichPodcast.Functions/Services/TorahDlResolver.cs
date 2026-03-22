using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using JoelRichPodcast.Functions.Models;
using Microsoft.Extensions.Logging;

namespace JoelRichPodcast.Functions.Services;

public partial class TorahDlResolver(ILogger<TorahDlResolver> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    /// <summary>
    /// Sites that are recognized but require authentication or are otherwise unsupported
    /// by torah-dl. URLs matching these patterns are skipped silently (Debug-level log)
    /// rather than producing Warning-level "Could not resolve" messages.
    /// </summary>
    [GeneratedRegex(@"https?://(?:www\.)?torahinmotion\.org/", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex UnsupportedSitePattern();

    /// <summary>
    /// Resolves a Torah website URL to a direct audio download URL using torah-dl.
    /// Falls back to the original URL if it's already a direct audio link.
    /// </summary>
    public async Task<TorahDlResult?> ResolveAsync(string url)
    {
        // Skip known-unsupported sites silently (no Warning, no alert noise)
        if (UnsupportedSitePattern().IsMatch(url))
        {
            logger.LogDebug("Skipping unsupported site: {Url}", url);
            return null;
        }

        // Fast path: direct audio file links don't need torah-dl
        if (IsDirectAudioLink(url))
        {
            var ext = Path.GetExtension(new Uri(url).AbsolutePath).TrimStart('.');
            var contentType = ext switch
            {
                "mp3" => "audio/mpeg",
                "m4a" => "audio/mp4",
                "wav" => "audio/wav",
                "ogg" => "audio/ogg",
                _ => "audio/mpeg"
            };
            return new TorahDlResult(url, null, contentType, Path.GetFileName(new Uri(url).AbsolutePath));
        }

        try
        {
            var scriptPath = GetScriptPath();
            var pythonLibsPath = GetPythonLibsPath();

            var startInfo = new ProcessStartInfo
            {
                FileName = GetPythonExecutable(),
                ArgumentList = { scriptPath, url },
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            // Add vendored python_libs to PYTHONPATH so torah-dl can be found
            if (Directory.Exists(pythonLibsPath))
            {
                var existingPythonPath = Environment.GetEnvironmentVariable("PYTHONPATH") ?? "";
                startInfo.Environment["PYTHONPATH"] = string.IsNullOrEmpty(existingPythonPath)
                    ? pythonLibsPath
                    : $"{pythonLibsPath}{Path.PathSeparator}{existingPythonPath}";
            }

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                logger.LogWarning("Failed to start python process for URL: {Url}", url);
                return null;
            }

            var stdout = await process.StandardOutput.ReadToEndAsync();
            var stderr = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                logger.LogWarning("torah-dl failed for {Url}: {Error}", url, stderr);
                return null;
            }

            var result = JsonSerializer.Deserialize<TorahDlResult>(stdout, JsonOptions);
            if (result?.DownloadUrl is null)
            {
                logger.LogWarning("torah-dl returned no download URL for {Url}: {Output}", url, stdout);
                return null;
            }

            logger.LogDebug("Resolved {Url} → {DownloadUrl}", url, result.DownloadUrl);
            return result;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "torah-dl resolution failed for {Url}", url);
            return null;
        }
    }

    /// <summary>
    /// Returns true if the URL belongs to a known-but-unsupported site (e.g. requires login).
    /// Used by the pipeline to decide log level when resolution returns null.
    /// </summary>
    public static bool IsUnsupportedSite(string url) => UnsupportedSitePattern().IsMatch(url);

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

    private static string GetPythonExecutable()
    {
        var configured = Environment.GetEnvironmentVariable("PYTHON_PATH");
        if (!string.IsNullOrEmpty(configured))
            return configured;

        // Linux (Azure Functions) typically has python3; Windows may use python
        return OperatingSystem.IsWindows() ? "python" : "python3";
    }

    private static string GetScriptPath()
    {
        // Script is deployed alongside the .NET output
        var baseDir = AppContext.BaseDirectory;
        return Path.Combine(baseDir, "python", "resolve_url.py");
    }

    private static string GetPythonLibsPath()
    {
        var baseDir = AppContext.BaseDirectory;
        return Path.Combine(baseDir, "python_libs");
    }
}
