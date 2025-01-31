
namespace JoelRichPodcast.Services;

internal class YUTorahLinkParser : OneHopLinkParser
{
    public YUTorahLinkParser(IHttpClientFactory httpClientFactory) : base(httpClientFactory)
    {
    }

    protected override IReadOnlyList<string> ValidUrls => new[]
    {
        "https://www.yutorah.org/lectures/",
        "https://yutorah.org/lectures/",
        "http://www.yutorah.org/sidebar/lecture.cfm/"
    };

    protected override string Selector => ".download a[title=\"Download this shiur\"]";
}