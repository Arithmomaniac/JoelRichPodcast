using System.Xml.Linq;
using JoelRichPodcast.Functions.Models;

namespace JoelRichPodcast.Functions.Services;

public class PodcastFeedGenerator
{
    private static readonly XNamespace Itunes = "http://www.itunes.com/dtds/podcast-1.0.dtd";
    private static readonly XNamespace Content = "http://purl.org/rss/1.0/modules/content/";
    private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";

    private const string FeedTitle = "Joel Rich Audio Roundup Podcast";
    private const string FeedDescription = "Joel Rich's Audio Roundup picks from Torah Musings — curated Torah audio from across the web, served as a podcast feed.";
    private const string FeedLink = "https://www.torahmusings.com/category/audio/";
    private const string FeedLanguage = "en";
    private const string FeedAuthor = "Joel Rich / Avi Levin";
    private const string FeedImage = "https://i0.wp.com/torahmusings.com/wp-content/uploads/2013/08/microphone.jpg";

    public string GenerateFeed(IEnumerable<EpisodeEntity> episodes, string selfUrl)
    {
        var items = episodes
            .OrderByDescending(e => e.RoundupUrl)
            .ThenBy(e => e.RoundupIndex ?? 0)
            .Take(200)
            .Select(BuildItem);

        var rss = new XElement("rss",
            new XAttribute("version", "2.0"),
            new XAttribute(XNamespace.Xmlns + "itunes", Itunes),
            new XAttribute(XNamespace.Xmlns + "content", Content),
            new XAttribute(XNamespace.Xmlns + "atom", Atom),
            new XElement("channel",
                new XElement("title", FeedTitle),
                new XElement("link", FeedLink),
                new XElement("description", FeedDescription),
                new XElement("language", FeedLanguage),
                new XElement("generator", "JoelRichPodcast"),
                new XElement(Atom + "link",
                    new XAttribute("href", selfUrl),
                    new XAttribute("rel", "self"),
                    new XAttribute("type", "application/rss+xml")),
                new XElement(Itunes + "author", FeedAuthor),
                new XElement(Itunes + "summary", FeedDescription),
                new XElement(Itunes + "image",
                    new XAttribute("href", FeedImage)),
                new XElement(Itunes + "category",
                    new XAttribute("text", "Religion & Spirituality"),
                    new XElement(Itunes + "category",
                        new XAttribute("text", "Judaism"))),
                new XElement(Itunes + "explicit", "no"),
                items));

        var doc = new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            rss);

        return doc.ToString();
    }

    private static XElement BuildItem(EpisodeEntity episode)
    {
        var description = episode.Description ?? episode.Title ?? "";

        var enclosureAttrs = new List<XAttribute>
        {
            new("url", episode.AudioUrl ?? ""),
            new("type", episode.AudioContentType ?? "audio/mpeg"),
        };
        if (episode.AudioContentLength.HasValue)
            enclosureAttrs.Add(new XAttribute("length", episode.AudioContentLength.Value));

        return new XElement("item",
            new XElement("title", episode.Title),
            new XElement("link", episode.SourceUrl),
            new XElement("description", description),
            new XElement("enclosure", enclosureAttrs),
            new XElement("guid",
                new XAttribute("isPermaLink", "false"),
                $"{episode.PartitionKey}:{episode.RowKey}"),
            new XElement("pubDate", (episode.PublishDate ?? DateTimeOffset.UtcNow).ToString("R")),
            new XElement(Itunes + "summary",
                description.Length > 4000 ? description[..4000] : description),
            new XElement(Itunes + "explicit", "no"));
    }
}
