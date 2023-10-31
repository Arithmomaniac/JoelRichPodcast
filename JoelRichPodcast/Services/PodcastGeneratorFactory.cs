using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using JoelRichPodcast.Models;
using Microsoft.Extensions.Logging;
using PodcastRssGenerator4DotNet;
using System.Xml.Linq;

namespace JoelRichPodcast.Services;

public class PodcastGeneratorFactory
{
    readonly JoelRichFeedGenerator _feedGenerator;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger _log;

    public PodcastGeneratorFactory(JoelRichFeedGenerator feedGenerator, IHttpClientFactory httpClientFactory, ILogger<PodcastGeneratorFactory> log)
    {
        _feedGenerator = feedGenerator;
        _httpClientFactory = httpClientFactory;
        _log = log;
    }

    public async Task<RssGenerator> GetPodcastGenerator()
    {
        XElement feed = await GetFeed();
        ParsedRSSFeedItem feedInfo = Parse(feed);
        return await _feedGenerator.GetPodcastGenerator(feedInfo);
    }

    private async Task<XElement> GetFeed()
    {
        var client = _httpClientFactory.CreateClient();
        var stream = await client.GetStreamAsync("http://www.torahmusings.com/category/audio/feed" + "?" + Guid.NewGuid());
        return await XElement.LoadAsync(stream, LoadOptions.None, CancellationToken.None);
    }

    private ParsedRSSFeedItem Parse(XElement feed)
    {
        XElement item = feed.Descendants("item").First(x => !x.Element("title")!.Value.Contains("Special"));
        DateTime dateUpdated = DateTime.Parse(item.Element("pubDate")!.Value);
        string link = item.Element("link")!.Value;
        string content = item.Element(XName.Get("encoded", "http://purl.org/rss/1.0/modules/content/"))!.Value;

        var document = new HtmlParser().ParseDocument(content);

        ParsedRSSFeedItem parsedRSSFeedItem = new ParsedRSSFeedItem
        {
            ItemLink = link,
            DateUpdated = dateUpdated,
            Links = document.QuerySelectorAll("li:has(a)").Select(ParseLink).ToList()
        };
        
        _log.LogInformation("Item parsed: {link} . {count} items", parsedRSSFeedItem.ItemLink, parsedRSSFeedItem.Links.Count);

        return parsedRSSFeedItem;
    }

    private static ParsedRSSFeedLink ParseLink(IElement linkNode)
    {        
        linkNode = (IElement)linkNode.Clone();
        var aNode = linkNode.QuerySelector("a")!;
        var (textContent, href) = (aNode.TextContent, aNode.Attributes["href"]!.Value);
        aNode.Remove();

        ParsedRSSFeedLink parsedRSSFeedLink = new ParsedRSSFeedLink(linkNode.TextContent.Trim(), textContent.Trim(), href.Trim());
        return parsedRSSFeedLink;
    }
}
