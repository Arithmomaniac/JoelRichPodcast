using PodcastRssGenerator4DotNet;
using JoelRichPodcast.Models;
using JoelRichPodcast.Abstract;
using AngleSharp;
using AngleSharp.Dom;

namespace JoelRichPodcast.Services;

internal class YUTorahLinkParser : ILinkParser
{
    static readonly IReadOnlyList<string> s_ValidUrls = new[]
    {
        "https://www.yutorah.org/lectures/",
        "https://yutorah.org/lectures/",
        "http://www.yutorah.org/sidebar/lecture.cfm/"
    };

    public async Task<Episode?> ParseLink(ParsedRSSFeedLink link)
    {
        if (!s_ValidUrls.Any(link.LinkURL.StartsWith))
            return null;

        if (await TryGetLinkFile(link.LinkURL) is not { } doc)
            return null;

        var downloadUrl = doc.QuerySelector(".download a[title=\"Download this shiur\"]")?.GetAttribute("href");
        if (downloadUrl is null)
            return null;

        return new Episode
        {
            FileUrl = downloadUrl,
            Permalink = link.LinkURL,
            Summary = link.Description,
            PublicationDate = DateTime.MinValue,
            Title = link.LinkTitle,
            FileType = "audio/mp3",
            Duration = "0:00"
        };
    }

    static async Task<IElement?> TryGetLinkFile(string linkURL)
    {
        try
        {
            using var browsingContext = BrowsingContext.New(Configuration.Default.WithDefaultLoader());
            var doc = await browsingContext.OpenAsync(linkURL);
            return (IElement)doc.DocumentElement.Clone();
        }
        catch
        {
            return null;
        }
    }
}