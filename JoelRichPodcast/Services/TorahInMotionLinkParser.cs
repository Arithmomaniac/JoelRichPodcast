namespace JoelRichPodcast.Services;

internal class TorahInMotionLinkParser : OneHopLinkParser
{
    protected override IReadOnlyList<string> ValidUrls => new[]
    {
        "https://torahinmotion.org/",
        "https://www.torahinmotion.org/"
    };

    protected override string Selector => "article .file-link > a";
}
