using PodcastRssGenerator4DotNet;
using JoelRichPodcast.Models;
using JoelRichPodcast.Abstract;
using AngleSharp;
using AngleSharp.Dom;

namespace JoelRichPodcast.Services;

internal abstract class OneHopLinkParser : ILinkParser
{
    protected abstract IReadOnlyList<string> ValidUrls { get; }

    protected abstract string Selector { get; }

    public async Task<Episode?> ParseLink(ParsedRSSFeedLink link)
    {
        if (!ValidUrls.Any(link.LinkURL.StartsWith))
            return null;

        if (await TryGetLinkFile(link.LinkURL) is not { } doc)
            return null;

        var downloadUrl = doc.QuerySelector(Selector)?.GetAttribute("href");
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