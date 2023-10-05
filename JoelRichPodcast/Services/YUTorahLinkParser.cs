using CsQuery;
using PodcastRssGenerator4DotNet;
using System;
using JoelRichPodcast.Models;
using JoelRichPodcast.Abstract;

namespace JoelRichPodcast.Services;

internal class YUTorahLinkParser : ILinkParser
{
    public Episode ParseLink(ParsedRSSFeedLink link)
    {
        if (!(link.LinkURL.StartsWith("https://www.yutorah.org/lectures/") || link.LinkURL.StartsWith("http://www.yutorah.org/sidebar/lecture.cfm/")))
            return null;

        if (!TryGetLinkFile(link.LinkURL, out CQ doc))
            return null;

        doc = doc[".download a[title=\"Download this shiur\"]"];
        string downloadurl = doc.Attr("href");
        if (downloadurl == null)
            return null;

        return new Episode
        {
            FileUrl = downloadurl,
            Permalink = link.LinkURL,
            Summary = link.Description,
            PublicationDate = DateTime.MinValue,
            Title = link.LinkTitle,
            FileType = "audio/mp3",
            Duration = "0:00"
        };
    }

    static bool TryGetLinkFile(string linkURL, out CQ doc)
    {
        try
        {
            doc = CQ.CreateFromUrl(linkURL, null);
            return true;
        }
        catch
        {
            doc = null;
            return false;
        }
    }
}