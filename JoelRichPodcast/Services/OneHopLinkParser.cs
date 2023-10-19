using PodcastRssGenerator4DotNet;
using JoelRichPodcast.Models;
using JoelRichPodcast.Abstract;
using AngleSharp;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace JoelRichPodcast.Services;

internal abstract class OneHopLinkParser : ILinkParser
{
    private IHttpClientFactory _httpClientFactory;

    protected OneHopLinkParser(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    protected abstract IReadOnlyList<string> ValidUrls { get; }

    protected abstract string Selector { get; }

    protected virtual bool UseBrowsingContext { get; } = false;

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

    async Task<IElement?> TryGetLinkFile(string linkURL)
    {
        try
        {
            // TODO: figure out secret sauce that makes this work more often
            if (UseBrowsingContext)
            {
                using var browsingContext = BrowsingContext.New(Configuration.Default.WithDefaultLoader());
                var doc = await browsingContext.OpenAsync(linkURL);
                return (IElement)doc.DocumentElement.Clone();
            }
            else
            {
                var client = _httpClientFactory.CreateClient();
                var stream = await client.GetStreamAsync(linkURL);
                return (await new HtmlParser().ParseDocumentAsync(stream)).DocumentElement;
            }
        }
        catch
        {
            return null;
        }
    }
}