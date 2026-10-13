namespace JoelRichPodcast.Backfill;

public sealed record BackfillManifest(IReadOnlyList<BackfillEpisode> Episodes);

public sealed record BackfillEpisode(
    string SourceUrl,
    string AudioUrl,
    string Title,
    string Description,
    DateTimeOffset PublishDate,
    string RoundupUrl,
    int RoundupIndex,
    string RowKey,
    string AudioContentType,
    long? AudioContentLength,
    string Provenance,
    bool Reviewed);
