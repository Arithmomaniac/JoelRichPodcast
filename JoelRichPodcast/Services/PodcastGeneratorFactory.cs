using CsQuery;
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
        return _feedGenerator.GetPodcastGenerator(feedInfo);
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
        ParsedRSSFeedItem parsedRSSFeedItem = new ParsedRSSFeedItem
        {
            ItemLink = link,
            DateUpdated = dateUpdated,
            Links = CQ.CreateFragment(content).Select("li").Has("a").Select(ParseLink).Where(x => x != null).ToList()
        };
        if (parsedRSSFeedItem is not null)
        {
            _log.LogInformation("Item parsed: {link} . {count} items", parsedRSSFeedItem.ItemLink, parsedRSSFeedItem.Links.Count);
        }
        return parsedRSSFeedItem;
    }

    private static ParsedRSSFeedLink ParseLink(IDomObject linkNode)
    {
        ParsedRSSFeedLink parsedRSSFeedLink;
        CQ linkCq = new CQ(linkNode.Clone());
        CQ aCq = linkCq.Find("a");
        if (aCq.Any())
        {
            ParsedRSSFeedLink parsedRSSFeedLink1 = new ParsedRSSFeedLink
            {
                LinkURL = aCq.Attr<string>("href"),
                LinkTitle = aCq.First().Text()
            };
            ParsedRSSFeedLink link = parsedRSSFeedLink1;
            aCq.FirstElement().Remove();
            link.Description = linkCq.Text().Trim();
            parsedRSSFeedLink = link;
        }
        else
        {
            parsedRSSFeedLink = null;
        }
        return parsedRSSFeedLink;
    }
}
