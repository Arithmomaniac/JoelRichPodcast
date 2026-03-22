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

        // 2. Resolve each link to a direct audio URL via torah-dl
        var resolved = 0;
        var httpClient = httpClientFactory.CreateClient("AudioMetadata");
        foreach (var link in links)
        {
            var result = await torahDl.ResolveAsync(link.LinkUrl);
            if (result?.DownloadUrl is null)
            {
                if (TorahDlResolver.IsUnsupportedSite(link.LinkUrl))
                    logger.LogDebug("Skipped unsupported site: {Url} ({Title})", link.LinkUrl, link.LinkTitle);
                else
                    logger.LogWarning("Could not resolve: {Url} ({Title})", link.LinkUrl, link.LinkTitle);
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
                RoundupUrl = link.RoundupUrl
            };

            // 3. Populate audio metadata via HTTP HEAD
            await PopulateAudioMetadataAsync(httpClient, episode);

            // 4. Compute RowKey from publish date + title
            episode.RowKey = EpisodeEntity.MakeRowKey(
                episode.PublishDate ?? DateTimeOffset.UtcNow, episode.Title ?? "untitled");

            await table.UpsertEntityAsync(episode, TableUpdateMode.Merge);
            resolved++;
        }
        logger.LogInformation("Resolved and upserted {Resolved}/{Total} episodes", resolved, links.Count);

        // 5. Read all episodes and generate RSS
        var allEpisodes = new List<EpisodeEntity>();
        await foreach (var entity in table.QueryAsync<EpisodeEntity>())
            allEpisodes.Add(entity);

        logger.LogInformation("Total episodes in store: {Count}", allEpisodes.Count);

        var feedXml = feedGenerator.GenerateFeed(allEpisodes, selfUrl: GetFeedUrl());

        // 6. Upload feed.xml to blob static website
        var container = blobService.GetBlobContainerClient(FeedContainer);
        await container.CreateIfNotExistsAsync();
        var blob = container.GetBlobClient(FeedBlobName);
        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(feedXml));
        await blob.UploadAsync(stream, new Azure.Storage.Blobs.Models.BlobHttpHeaders
        {
            ContentType = "application/rss+xml; charset=utf-8"
        });

        logger.LogInformation("Published feed.xml with {Count} episodes", allEpisodes.Count);
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
            logger.LogDebug(ex, "HEAD failed for {Url}", episode.AudioUrl);
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
