
namespace JoelRichPodcast.Services;

internal class PodbeanLinkParser : OneHopLinkParser
{
    public PodbeanLinkParser(IHttpClientFactory httpClientFactory) : base(httpClientFactory)
    {
    }

    protected override bool UseBrowsingContext { get; } = true;

    protected override IReadOnlyList<string> ValidUrls => new[]
    {
        "https://www.podbean.com/site/EpisodeDownload/",
    };

    protected override string Selector => ".podcast-media .download-btn";
}
