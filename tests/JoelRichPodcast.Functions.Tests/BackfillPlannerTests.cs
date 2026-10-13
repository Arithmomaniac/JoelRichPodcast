using JoelRichPodcast.Backfill;
using JoelRichPodcast.Functions.Models;
using Xunit;

namespace JoelRichPodcast.Functions.Tests;

public class BackfillPlannerTests
{
    [Fact]
    public void Plan_IsIdempotentBySourceUrl()
    {
        var candidate = CreateCandidate();
        var existing = CreateExisting(candidate);

        var result = Assert.Single(BackfillPlanner.Plan([candidate], [existing]));

        Assert.Equal(BackfillDisposition.AlreadyExists, result.Disposition);
        Assert.Same(existing, result.ExistingEpisode);
    }

    [Fact]
    public void Plan_BlocksRowKeyCollision()
    {
        var candidate = CreateCandidate();
        var existing = new EpisodeEntity
        {
            RowKey = candidate.RowKey,
            SourceUrl = "https://example.test/another-source"
        };

        var result = Assert.Single(BackfillPlanner.Plan([candidate], [existing]));

        Assert.Equal(BackfillDisposition.Conflict, result.Disposition);
    }

    private static BackfillEpisode CreateCandidate() =>
        new(
            "https://example.test/source",
            "https://media.example.test/audio.mp3",
            "Title",
            "Description",
            new DateTimeOffset(2026, 9, 17, 0, 0, 0, TimeSpan.Zero),
            "https://example.test/roundup",
            0,
            "2026-09-17_title",
            "audio/mpeg",
            123,
            "Test fixture",
            true);

    private static EpisodeEntity CreateExisting(BackfillEpisode candidate) =>
        new()
        {
            RowKey = candidate.RowKey,
            SourceUrl = candidate.SourceUrl,
            AudioUrl = candidate.AudioUrl,
            Title = candidate.Title
        };
}
