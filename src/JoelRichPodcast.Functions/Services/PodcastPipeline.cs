using Azure.Data.Tables;
using Azure.Storage.Blobs;
using JoelRichPodcast.Functions.Models;
using Microsoft.Extensions.Logging;

namespace JoelRichPodcast.Functions.Services;

/// <summary>
/// Shared pipeline logic used by both the timer and HTTP trigger functions.
/// </summary>
public class PodcastPipeline(
    TorahMusingsFeedParser feedParser,
    TorahDlResolver torahDl,
    PodcastFeedGenerator feedGenerator,
    TableServiceClient tableService,
    BlobServiceClient blobService,
    IHttpClientFactory httpClientFactory,
    ILogger<PodcastPipeline> logger)
{
    private const string EpisodesTable = "episodes";
    private const string FeedContainer = "$web";
    private const string FeedBlobName = "feed.xml";

    public async Task<string> RunAsync()
    {
        var table = tableService.GetTableClient(EpisodesTable);
        await table.CreateIfNotExistsAsync();

        // 1. Parse Torah Musings Audio Roundup RSS
        var links = await feedParser.ParseLatestRoundupAsync();

        if (links.Count == 0)
        {
            logger.LogWarning("No links found in Audio Roundup — feed may be empty or format changed");
        }

        // 2. Resolve all links to direct audio URLs via torah-dl (batch)
        var resolved = 0;
        var failed = 0;
        var httpClient = httpClientFactory.CreateClient("AudioMetadata");

        var linkUrls = links.Select(l => l.LinkUrl).ToList();
        var resolutions = await torahDl.ResolveBatchAsync(linkUrls);

        foreach (var link in links)
        {
            var result = resolutions.GetValueOrDefault(link.LinkUrl);
            if (result?.DownloadUrl is null)
            {
                if (TorahDlResolver.IsUnsupportedSite(link.LinkUrl))
                    logger.LogDebug("Skipped unsupported site: {Url} ({Title})", link.LinkUrl, link.LinkTitle);
                else
                    logger.LogWarning("Could not resolve: {Url} ({Title})", link.LinkUrl, link.LinkTitle);
                failed++;
                continue;
            }

            var episode = new EpisodeEntity
            {
                Title = result.Title ?? link.LinkTitle,
                AudioUrl = result.DownloadUrl,
                AudioContentType = result.FileFormat ?? "audio/mpeg",
                SourceUrl = link.LinkUrl,
                Description = string.IsNullOrWhiteSpace(link.Description)
                    ? link.LinkTitle
                    : $"{link.LinkTitle} — {link.Description}",
                PublishDate = link.PublishDate,
                RoundupUrl = link.RoundupUrl,
                RoundupIndex = link.RoundupIndex
            };

            // 3. Populate audio metadata via HTTP HEAD
            await PopulateAudioMetadataAsync(httpClient, episode);

            // 4. Compute RowKey from publish date + title
            episode.RowKey = EpisodeEntity.MakeRowKey(
                episode.PublishDate ?? DateTimeOffset.UtcNow, episode.Title ?? "untitled");

            try
            {
                await table.UpsertEntityAsync(episode, TableUpdateMode.Merge);
                resolved++;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Table Storage upsert failed for {Title} ({Url})", episode.Title, episode.AudioUrl);
            }
        }
        logger.LogInformation("Resolved and upserted {Resolved}/{Total} episodes ({Failed} failed to resolve)", resolved, links.Count, failed);

        if (resolved == 0 && links.Count > 0)
        {
            logger.LogError("Zero episodes resolved out of {Total} links — all torah-dl resolutions failed", links.Count);
        }
        else if (failed > 0 && failed > links.Count / 2)
        {
            logger.LogError("High failure rate: {Failed}/{Total} links failed to resolve — possible torah-dl or site issue", failed, links.Count);
        }

        // 5. Read all episodes and generate RSS
        var allEpisodes = new List<EpisodeEntity>();
        await foreach (var entity in table.QueryAsync<EpisodeEntity>())
            allEpisodes.Add(entity);

        logger.LogInformation("Total episodes in store: {Count}", allEpisodes.Count);

        var feedXml = feedGenerator.GenerateFeed(allEpisodes, selfUrl: GetFeedUrl());

        // 6. Upload feed.xml to blob static website
        try
        {
            var container = blobService.GetBlobContainerClient(FeedContainer);
            await container.CreateIfNotExistsAsync();
            var blob = container.GetBlobClient(FeedBlobName);
            using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(feedXml));
            await blob.UploadAsync(stream, new Azure.Storage.Blobs.Models.BlobHttpHeaders
            {
                ContentType = "application/rss+xml; charset=utf-8"
            });
            logger.LogInformation("Published feed.xml with {Count} episodes", allEpisodes.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to upload feed.xml to blob storage");
            throw;
        }

        return feedXml;
    }

    private async Task PopulateAudioMetadataAsync(HttpClient httpClient, EpisodeEntity episode)
    {
        if (string.IsNullOrWhiteSpace(episode.AudioUrl))
            return;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, episode.AudioUrl);
            using var response = await httpClient.SendAsync(request);

            if (response.IsSuccessStatusCode)
            {
                if (response.Content.Headers.ContentLength is { } length)
                    episode.AudioContentLength = length;

                if (response.Content.Headers.ContentType?.MediaType is { } mediaType)
                    episode.AudioContentType = mediaType;

                if (response.Content.Headers.LastModified is { } lastMod)
                    episode.PublishDate = lastMod;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "HEAD request failed for {Url}", episode.AudioUrl);
        }
    }

    private static string GetFeedUrl()
    {
        var staticWebsiteUrl = Environment.GetEnvironmentVariable("StaticWebsiteUrl");
        if (!string.IsNullOrWhiteSpace(staticWebsiteUrl))
            return $"{staticWebsiteUrl.TrimEnd('/')}/{FeedBlobName}";

        var storageAccount = Environment.GetEnvironmentVariable("StorageAccountName") ?? "joelrichst";
        return $"https://{storageAccount}.z13.web.core.windows.net/{FeedBlobName}";
    }
}
