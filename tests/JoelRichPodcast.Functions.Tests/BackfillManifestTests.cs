using System.Text.Json;
using JoelRichPodcast.Backfill;
using JoelRichPodcast.Functions.Models;
using Xunit;

namespace JoelRichPodcast.Functions.Tests;

public class BackfillManifestTests
{
    [Fact]
    public async Task Manifest_HasUniqueSourcesAndPinnedProductionRowKeys()
    {
        await using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "resolution-backfill.json"));
        var manifest = await JsonSerializer.DeserializeAsync<BackfillManifest>(stream, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
        }, TestContext.Current.CancellationToken);

        Assert.NotNull(manifest);
        Assert.Equal(5, manifest.Episodes.Count);
        Assert.Equal(
            manifest.Episodes.Count,
            manifest.Episodes.Select(episode => episode.SourceUrl).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(manifest.Episodes, episode =>
        {
            Assert.Equal(EpisodeEntity.MakeRowKey(episode.PublishDate, episode.Title), episode.RowKey);
            Assert.False(episode.Reviewed);
        });
    }
}
