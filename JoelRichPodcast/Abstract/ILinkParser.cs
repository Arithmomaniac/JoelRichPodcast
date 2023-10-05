using JoelRichPodcast.Models;
using PodcastRssGenerator4DotNet;

namespace JoelRichPodcast.Abstract;

public interface ILinkParser
{
    Episode ParseLink(ParsedRSSFeedLink link);
}