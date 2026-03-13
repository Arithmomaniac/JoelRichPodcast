using System.Xml.Linq;
using AngleSharp.Html.Parser;
using JoelRichPodcast.Functions.Models;
using Microsoft.Extensions.Logging;

namespace JoelRichPodcast.Functions.Services;

public class TorahMusingsFeedParser(
    IHttpClientFactory httpClientFactory,
    ILogger<TorahMusingsFeedParser> logger)
{
    private const string FeedUrl = "https://www.torahmusings.com/category/audio/feed";

    /// <summary>
    /// Fetches the latest Audio Roundup post from Torah Musings RSS and extracts all shiur links.
    /// </summary>
    public async Task<List<AudioRoundupLink>> ParseLatestRoundupAsync()
    {
        var client = httpClientFactory.CreateClient("TorahMusings");
        var feedStream = await client.GetStreamAsync($"{FeedUrl}?{Guid.NewGuid()}");
        var feed = await XElement.LoadAsync(feedStream, LoadOptions.None, CancellationToken.None);

        // Find the first Audio Roundup item (skip "Special" posts)
        var item = feed.Descendants("item")
            .FirstOrDefault(x => !x.Element("title")!.Value.Contains("Special", StringComparison.OrdinalIgnoreCase));

        if (item is null)
        {
            logger.LogWarning("No Audio Roundup item found in feed");
            return [];
        }

        var dateUpdated = DateTimeOffset.Parse(item.Element("pubDate")!.Value);
        var roundupUrl = item.Element("link")!.Value;
        var contentNs = XName.Get("encoded", "http://purl.org/rss/1.0/modules/content/");
        var htmlContent = item.Element(contentNs)!.Value;

        logger.LogInformation("Parsing Audio Roundup: {Url} ({Date})", roundupUrl, dateUpdated);

        var links = ParseHtmlLinks(htmlContent, dateUpdated, roundupUrl);
        logger.LogInformation("Found {Count} links in Audio Roundup", links.Count);
        return links;
    }

    private static List<AudioRoundupLink> ParseHtmlLinks(
        string htmlContent, DateTimeOffset publishDate, string roundupUrl)
    {
        var document = new HtmlParser().ParseDocument(htmlContent);
        var results = new List<AudioRoundupLink>();

        foreach (var li in document.QuerySelectorAll("li:has(a)"))
        {
            var clone = (AngleSharp.Dom.IElement)li.Clone();
            var anchor = clone.QuerySelector("a");
            if (anchor is null) continue;

            var linkUrl = anchor.GetAttribute("href")?.Trim();
            var linkTitle = anchor.TextContent.Trim();

            if (string.IsNullOrWhiteSpace(linkUrl))
                continue;

            // Remove the anchor to get the surrounding description text
            anchor.Remove();
            var description = clone.TextContent.Trim();

            results.Add(new AudioRoundupLink(
                Description: description,
                LinkTitle: linkTitle,
                LinkUrl: linkUrl,
                PublishDate: publishDate,
                RoundupUrl: roundupUrl));
        }

        return results;
    }
}
