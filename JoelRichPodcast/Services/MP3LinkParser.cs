using PodcastRssGenerator4DotNet;
using JoelRichPodcast.Models;
using JoelRichPodcast.Abstract;

namespace JoelRichPodcast.Services;

public class MP3LinkParser : ILinkParser
{
    public Task<Episode?> ParseLink(ParsedRSSFeedLink link)
    {
        string? fileType = link.LinkURL.Split('.').LastOrDefault() switch
        {
            "mp3" => "audio/mp3",
            "m4a" => "audio/m4a",
            _ => null
        };

        if (fileType == null)
            return Task.FromResult<Episode?>(null);


        return Task.FromResult<Episode?>(new Episode
        {
            FileUrl = link.LinkURL,
            Permalink = link.LinkURL,
            Summary = link.Description,
            PublicationDate = DateTime.MinValue,
            Title = link.LinkTitle,
            FileType = fileType,
            Duration = "0:00",
        });
    }
}