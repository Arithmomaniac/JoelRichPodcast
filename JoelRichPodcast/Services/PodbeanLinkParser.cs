namespace JoelRichPodcast.Services;

internal class PodbeanLinkParser : OneHopLinkParser
{
    protected override IReadOnlyList<string> ValidUrls => new[]
    {
        "https://www.podbean.com/site/EpisodeDownload/",
    };

    protected override string Selector => ".podcast-media .download-btn";
}
