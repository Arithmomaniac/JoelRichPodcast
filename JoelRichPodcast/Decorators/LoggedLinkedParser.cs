using JoelRichPodcast.Abstract;
using JoelRichPodcast.Models;
using Microsoft.Extensions.Logging;
using PodcastRssGenerator4DotNet;

namespace JoelRichPodcast.Decorators;


public class LoggedLinkedParser : ILinkParser
{
    private readonly ILogger _log;
    private readonly ILinkParser _parser;
    private readonly string _parserClass;

    public LoggedLinkedParser(ILinkParser parser, ILogger<LoggedLinkedParser> log)
    {
        _parser = parser;
        _parserClass = parser.GetType().Name;
        _log = log;
    }

    public async Task<Episode> ParseLink(ParsedRSSFeedLink link)
    {
        var episode = await _parser.ParseLink(link);
        var parsedVerbiage = episode == null ? "could NOT parse" : "PARSED";
        _log.LogDebug("{parserClass} {parsedVerbiage} {linkURL}", _parserClass, parsedVerbiage, link.LinkURL);
        return episode;
    }
}