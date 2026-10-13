namespace JoelRichPodcast.Functions.Models;

public record TorahDlResult(
    string? DownloadUrl,
    string? Title,
    string? FileFormat,
    string? FileName,
    string? ReasonCode = null,
    string? Error = null);
