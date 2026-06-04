using System.Text;
using System.Xml.Linq;
using AngleSharp.Dom;
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

        // Old format: <li><a href="URL">Title</a> Description</li>
        foreach (var li in document.QuerySelectorAll("li:has(a)"))
        {
            var clone = (AngleSharp.Dom.IElement)li.Clone();
            var anchor = clone.QuerySelector("a");
            if (anchor is null) continue;

            var linkUrl = anchor.GetAttribute("href")?.Trim();
            var linkTitle = anchor.TextContent.Trim();

            if (string.IsNullOrWhiteSpace(linkUrl))
                continue;

            anchor.Remove();
            var description = clone.TextContent.Trim();

            results.Add(new AudioRoundupLink(
                Description: description,
                LinkTitle: linkTitle,
                LinkUrl: linkUrl,
                PublishDate: publishDate,
                RoundupUrl: roundupUrl,
                RoundupIndex: results.Count));
        }

        // Paragraph format: <p><a href="URL">URL</a><br>Title<br>Description</p>.
        // Some posts put multiple entries in one paragraph, separated by anchors.
        // Some posts wrap the title in <strong>; newer posts leave it as plain text.
        foreach (var p in document.QuerySelectorAll("p:has(a):has(br)").OfType<IElement>())
        {
            if (p.Closest("li") is not null)
                continue;

            foreach (var (linkUrl, linkTitle, description) in ParseParagraphEntries(p))
                results.Add(new AudioRoundupLink(
                    Description: description,
                    LinkTitle: linkTitle,
                    LinkUrl: linkUrl,
                    PublishDate: publishDate,
                    RoundupUrl: roundupUrl,
                    RoundupIndex: results.Count));
        }

        // Split-paragraph format:
        // <p><a href="URL">URL</a></p><p>Title</p><p>Description</p>, or
        // <p>URL</p><p>Title</p><p>Description</p> when the feed stops linking URLs.
        // Each URL-only paragraph starts a new entry; following text-only
        // paragraphs become the title and description until the next URL paragraph.
        foreach (var (linkUrl, linkTitle, description) in ParseSplitParagraphEntries(document))
            results.Add(new AudioRoundupLink(
                Description: description,
                LinkTitle: linkTitle,
                LinkUrl: linkUrl,
                PublishDate: publishDate,
                RoundupUrl: roundupUrl,
                RoundupIndex: results.Count));

        return results;
    }

    private static IEnumerable<(string LinkUrl, string LinkTitle, string Description)> ParseParagraphEntries(IElement paragraph)
    {
        string? linkUrl = null;
        var lines = new List<string>();
        var currentLine = new StringBuilder();

        // In paragraph-based posts, each <a> starts a new entry and each <br> separates
        // the URL, title, and description lines, even when many entries share one <p>.
        foreach (var node in WalkEntryNodes(paragraph))
        {
            if (node is IElement { LocalName: "a" } anchor)
            {
                var entry = BuildEntry(linkUrl, lines, currentLine);
                if (entry is not null)
                    yield return entry.Value;

                linkUrl = anchor.GetAttribute("href")?.Trim();
                lines = [];
                currentLine.Clear();
                continue;
            }

            if (linkUrl is null)
                continue;

            if (node is IElement { LocalName: "br" })
            {
                AddCurrentLine(lines, currentLine);
                continue;
            }

            if (node is IText text)
                currentLine.Append(text.Data);
        }

        var finalEntry = BuildEntry(linkUrl, lines, currentLine);
        if (finalEntry is not null)
            yield return finalEntry.Value;
    }

    private static IEnumerable<INode> WalkEntryNodes(INode node)
    {
        foreach (var child in node.ChildNodes)
        {
            // Preserve source order while flattening formatting spans around text.
            if (child is IElement { LocalName: "a" or "br" } or IText)
            {
                yield return child;
                continue;
            }

            foreach (var descendant in WalkEntryNodes(child))
                yield return descendant;
        }
    }

    private static IEnumerable<(string LinkUrl, string LinkTitle, string Description)> ParseSplitParagraphEntries(IDocument document)
    {
        string? linkUrl = null;
        var lines = new List<string>();

        foreach (var paragraph in document.QuerySelectorAll("p").OfType<IElement>())
        {
            if (paragraph.Closest("li") is not null)
                continue;

            var paragraphUrl = GetUrlOnlyParagraphUrl(paragraph);
            if (paragraphUrl is not null)
            {
                var entry = BuildEntry(linkUrl, lines);
                if (entry is not null)
                    yield return entry.Value;

                linkUrl = paragraphUrl;
                lines = [];
                continue;
            }

            if (linkUrl is null)
                continue;

            if (paragraph.QuerySelector("a") is not null)
                continue;

            if (paragraph.QuerySelector("br") is not null)
                lines.AddRange(ReadParagraphTextLines(paragraph));
            else
            {
                var line = NormalizeWhitespace(paragraph.TextContent);
                if (!string.IsNullOrWhiteSpace(line))
                    lines.Add(line);
            }
        }

        var finalEntry = BuildEntry(linkUrl, lines);
        if (finalEntry is not null)
            yield return finalEntry.Value;
    }

    private static string? GetUrlOnlyParagraphUrl(IElement paragraph)
    {
        var anchors = paragraph.QuerySelectorAll("a").OfType<IElement>().ToList();
        var paragraphText = NormalizeWhitespace(paragraph.TextContent);

        if (anchors.Count == 0)
            return IsHttpUrl(paragraphText) ? paragraphText : null;

        if (anchors.Count != 1)
            return null;

        var anchor = anchors[0];
        var linkUrl = anchor.GetAttribute("href")?.Trim();
        if (string.IsNullOrWhiteSpace(linkUrl))
            return null;

        var anchorText = NormalizeWhitespace(anchor.TextContent);
        if (!IsHttpUrl(anchorText) || !string.Equals(paragraphText, anchorText, StringComparison.OrdinalIgnoreCase))
            return null;

        return linkUrl;
    }

    private static IEnumerable<string> ReadParagraphTextLines(IElement paragraph)
    {
        var currentLine = new StringBuilder();
        foreach (var node in WalkEntryNodes(paragraph))
        {
            if (node is IElement { LocalName: "br" })
            {
                var line = NormalizeWhitespace(currentLine.ToString());
                if (!string.IsNullOrWhiteSpace(line))
                    yield return line;

                currentLine.Clear();
                continue;
            }

            if (node is IText text)
                currentLine.Append(text.Data);
        }

        var finalLine = NormalizeWhitespace(currentLine.ToString());
        if (!string.IsNullOrWhiteSpace(finalLine))
            yield return finalLine;
    }

    private static (string LinkUrl, string LinkTitle, string Description)? BuildEntry(
        string? linkUrl,
        List<string> lines,
        StringBuilder currentLine)
    {
        if (string.IsNullOrWhiteSpace(linkUrl))
            return null;

        AddCurrentLine(lines, currentLine);
        if (lines.Count == 0)
            return null;

        var linkTitle = lines[0];
        var description = string.Join(" ", lines.Skip(1)).Trim();
        return (linkUrl, linkTitle, description);
    }

    private static (string LinkUrl, string LinkTitle, string Description)? BuildEntry(
        string? linkUrl,
        List<string> lines)
    {
        if (string.IsNullOrWhiteSpace(linkUrl) || lines.Count == 0)
            return null;

        var linkTitle = lines[0];
        var description = string.Join(" ", lines.Skip(1)).Trim();
        return (linkUrl, linkTitle, description);
    }

    private static void AddCurrentLine(List<string> lines, StringBuilder currentLine)
    {
        var line = NormalizeWhitespace(currentLine.ToString());
        if (!string.IsNullOrWhiteSpace(line))
            lines.Add(line);

        currentLine.Clear();
    }

    private static string NormalizeWhitespace(string value)
    {
        return string.Join(' ', value.Split([' ', '\t', '\r', '\n', '\f'], StringSplitOptions.RemoveEmptyEntries));
    }

    private static bool IsHttpUrl(string value)
    {
        return value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
    }
}
