using JoelRichPodcast.Models;
using PodcastRssGenerator4DotNet;

namespace JoelRichPodcast.Abstract;

public interface ILinkParser
{
    Task<Episode> ParseLink(ParsedRSSFeedLink link);
}