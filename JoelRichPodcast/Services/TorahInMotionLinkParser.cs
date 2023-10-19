
namespace JoelRichPodcast.Services;

internal class TorahInMotionLinkParser : OneHopLinkParser
{
    public TorahInMotionLinkParser(IHttpClientFactory httpClientFactory) : base(httpClientFactory)
    {
    }

    protected override IReadOnlyList<string> ValidUrls => new[]
    {
        "https://torahinmotion.org/",
        "https://www.torahinmotion.org/"
    };

    protected override string Selector => "article .file-link > a";
}
