using System.Security.Cryptography;
using System.Text.Json;
using Azure.Data.Tables;
using Azure.Identity;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using JoelRichPodcast.Functions.Models;
using JoelRichPodcast.Functions.Services;

namespace JoelRichPodcast.Backfill;

internal static class Program
{
    private const string EpisodesTable = "episodes";
    private const string FeedContainer = "$web";
    private const string FeedBlobName = "feed.xml";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    public static async Task<int> Main(string[] args)
    {
        var options = BackfillOptions.Parse(args);
        var manifestBytes = await File.ReadAllBytesAsync(options.ManifestPath);
        var manifest = JsonSerializer.Deserialize<BackfillManifest>(manifestBytes, JsonOptions)
            ?? throw new InvalidOperationException("The manifest could not be deserialized.");
        var manifestHash = Convert.ToHexStringLower(SHA256.HashData(manifestBytes));

        var credential = new DefaultAzureCredential();
        var tableService = new TableServiceClient(
            new Uri($"https://{options.StorageAccountName}.table.core.windows.net"),
            credential);
        var table = tableService.GetTableClient(EpisodesTable);
        var existingEpisodes = new List<EpisodeEntity>();
        await foreach (var episode in table.QueryAsync<EpisodeEntity>())
            existingEpisodes.Add(episode);

        var plan = BackfillPlanner.Plan(manifest.Episodes, existingEpisodes);
        foreach (var item in plan)
        {
            var detail = string.IsNullOrWhiteSpace(item.Detail) ? "" : $": {item.Detail}";
            Console.WriteLine($"{item.Disposition}: {item.Candidate.SourceUrl}{detail}");
        }

        Console.WriteLine($"Manifest SHA-256: {manifestHash}");
        Console.WriteLine($"Adds: {plan.Count(item => item.Disposition == BackfillDisposition.Add)}; "
            + $"existing: {plan.Count(item => item.Disposition == BackfillDisposition.AlreadyExists)}; "
            + $"conflicts: {plan.Count(item => item.Disposition == BackfillDisposition.Conflict)}");

        if (plan.Any(item => item.Disposition == BackfillDisposition.Conflict))
            return 2;
        if (!options.Apply)
        {
            Console.WriteLine("Dry run only. No storage or feed changes were made.");
            return 0;
        }
        if (manifest.Episodes.Any(episode => !episode.Reviewed))
            throw new InvalidOperationException("Every manifest episode must be marked reviewed before applying.");
        if (!string.Equals(options.ConfirmedManifestHash, manifestHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Pass the displayed manifest hash with --confirm-manifest-sha256.");

        var additions = plan
            .Where(item => item.Disposition == BackfillDisposition.Add)
            .Select(item => CreateEntity(item.Candidate))
            .ToList();
        foreach (var episode in additions)
            await table.AddEntityAsync(episode);

        if (additions.Count == 0)
        {
            Console.WriteLine("No new episodes were required. Existing rows and feed were unchanged.");
            return 0;
        }

        var allEpisodes = new List<EpisodeEntity>();
        await foreach (var episode in table.QueryAsync<EpisodeEntity>())
            allEpisodes.Add(episode);

        var feedUrl = $"https://{options.StorageAccountName}.z13.web.core.windows.net/{FeedBlobName}";
        var feedXml = new PodcastFeedGenerator().GenerateFeed(allEpisodes, feedUrl);
        var blobService = new BlobServiceClient(
            new Uri($"https://{options.StorageAccountName}.blob.core.windows.net"),
            credential);
        var blob = blobService.GetBlobContainerClient(FeedContainer).GetBlobClient(FeedBlobName);
        await using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(feedXml));
        await blob.UploadAsync(stream, new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = "application/rss+xml; charset=utf-8" }
        });

        Console.WriteLine($"Added {additions.Count} episodes and republished the feed.");
        return 0;
    }

    private static EpisodeEntity CreateEntity(BackfillEpisode episode) =>
        new()
        {
            PartitionKey = "roundup",
            RowKey = episode.RowKey,
            SourceUrl = episode.SourceUrl,
            AudioUrl = episode.AudioUrl,
            Title = episode.Title,
            Description = episode.Description,
            PublishDate = episode.PublishDate,
            RoundupUrl = episode.RoundupUrl,
            RoundupIndex = episode.RoundupIndex,
            AudioContentType = episode.AudioContentType,
            AudioContentLength = episode.AudioContentLength
        };
}

internal sealed record BackfillOptions(
    string ManifestPath,
    string StorageAccountName,
    bool Apply,
    string? ConfirmedManifestHash)
{
    public static BackfillOptions Parse(string[] args)
    {
        var manifestPath = Path.GetFullPath(Path.Combine("scripts", "resolution-backfill.json"));
        var storageAccountName = "joelrichpodcastst";
        var apply = false;
        string? confirmedManifestHash = null;

        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--manifest":
                    manifestPath = Path.GetFullPath(NextValue(args, ref index));
                    break;
                case "--storage-account":
                    storageAccountName = NextValue(args, ref index);
                    break;
                case "--apply":
                    apply = true;
                    break;
                case "--confirm-manifest-sha256":
                    confirmedManifestHash = NextValue(args, ref index);
                    break;
                default:
                    throw new ArgumentException($"Unknown argument: {args[index]}");
            }
        }

        return new BackfillOptions(manifestPath, storageAccountName, apply, confirmedManifestHash);
    }

    private static string NextValue(string[] args, ref int index)
    {
        index++;
        if (index >= args.Length)
            throw new ArgumentException("An option value is missing.");
        return args[index];
    }
}
