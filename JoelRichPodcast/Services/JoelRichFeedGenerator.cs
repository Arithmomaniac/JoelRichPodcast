using PodcastRssGenerator4DotNet;
using JoelRichPodcast.Models;
using JoelRichPodcast.Abstract;
using Microsoft.Extensions.Logging;

namespace JoelRichPodcast.Services;

public class JoelRichFeedGenerator
{
    private readonly IReadOnlyList<ILinkParser> _parsers;
    private readonly ILogger<JoelRichFeedGenerator> _log;

    public JoelRichFeedGenerator(IEnumerable<ILinkParser> parsers, ILogger<JoelRichFeedGenerator> log)
    {
        if (parsers?.Any() != true)
            throw new ArgumentException("parsers are required", nameof(parsers));
        _parsers = parsers.ToArray();
       _log = log;

       _log.LogDebug("Parsers: {parsers}", _parsers.Select(p => p.GetType().Name));
    }

    public async Task<RssGenerator> GetPodcastGenerator(ParsedRSSFeedItem items)
    {
        RssGenerator rssGenerator = new RssGenerator
        {

            Title = "Joel Rich Audio Roundup Podcast",
            Description = "Joel Rich's famous Audio Roundup picks as a podcast. (Beta)",
            HomepageUrl = "http://www.torahmusings.com/category/audio/",
            AuthorName = "Joel Rich / Avi Levin",
            AuthorEmail = "email@avilevin.net",
            Episodes = new List<Episode>(),
            ImageUrl = "http://i0.wp.com/torahmusings.com/wp-content/uploads/2013/08/microphone.jpg",
            iTunesCategory = "Temp",
            iTunesSubCategory = "Temp",
        };

        foreach (ParsedRSSFeedLink item in items.Links)
        {
            Episode? episode = null;
            foreach (ILinkParser parser in _parsers)
            {
                episode = await parser.ParseLink(item);
                if (episode is not null)
                {
                    _log.LogDebug("{item} parsed with {parser}", item.LinkURL, parser.GetType().Name);
                    break;
                }
            }

            if (episode is null)
            {
                _log.LogWarning("{item} not parsed by any parser", item.LinkURL);
            }
            else
            {
                rssGenerator.Episodes.Add(episode);
            }
        }

        _log.LogInformation("{count} items parsed", rssGenerator.Episodes.Count);
        return rssGenerator;
    }
}