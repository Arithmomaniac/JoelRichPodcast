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
    private readonly ILogger _log;

    public PodcastGeneratorFactory(JoelRichFeedGenerator feedGenerator, ILogger<PodcastGeneratorFactory> log)
    {
        _feedGenerator = feedGenerator;
        _log = log;
    }

    public async Task<RssGenerator> GetPodcastGenerator()
    {
        XElement feed = await GetFeed();
        ParsedRSSFeedItem feedInfo = Parse(feed);
        return await _feedGenerator.GetPodcastGenerator(feedInfo);
    }

    private static async Task<XElement> GetFeed()
    {
        XElement xElement;
        using var client = new HttpClient();
        using HttpResponseMessage response = await client.GetAsync("http://www.torahmusings.com/category/audio/feed" + "?" + Guid.NewGuid());
        using var responseContent = response.Content;
        var stream = await responseContent.ReadAsStringAsync();
        xElement = XElement.Parse(stream);
        return xElement;
    }

    private ParsedRSSFeedItem Parse(XElement feed)
    {
        XElement item = feed.Descendants("item").First(x => !x.Element("title")!.Value.Contains("Special"));
        DateTime dateUpdated = DateTime.Parse(item.Element("pubDate")!.Value);
        string link = item.Element("link")!.Value;
        string content = item.Element(XName.Get("encoded", "http://purl.org/rss/1.0/modules/content/"))!.Value;

        var parser = new HtmlParser();
        var document = parser.ParseDocument(content);

        ParsedRSSFeedItem parsedRSSFeedItem = new ParsedRSSFeedItem
        {
            ItemLink = link,
            DateUpdated = dateUpdated,
            Links = document.QuerySelectorAll("li:has(a)").Select(ParseLink).ToList()
        };
        if (parsedRSSFeedItem is not null)
        {
            _log.LogInformation("Item parsed: {link} . {count} items", parsedRSSFeedItem.ItemLink, parsedRSSFeedItem.Links.Count);
        }
        return parsedRSSFeedItem;
    }

    private static ParsedRSSFeedLink ParseLink(IElement linkNode)
    {        
        linkNode = (IElement)linkNode.Clone();
        var aNode = linkNode.QuerySelector("a")!;

        ParsedRSSFeedLink parsedRSSFeedLink = new ParsedRSSFeedLink
        {
            LinkURL = aNode.Attributes["href"]!.Value,
            LinkTitle = aNode.TextContent
        };
        aNode.Remove();
        parsedRSSFeedLink.Description = linkNode.TextContent.Trim();
        return parsedRSSFeedLink;
    }
}
