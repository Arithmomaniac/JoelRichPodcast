using PodcastRssGenerator4DotNet;
using System;
using JoelRichPodcast.Models;
using JoelRichPodcast.Abstract;

namespace JoelRichPodcast.Services;

public class MP3LinkParser : ILinkParser
{
    public Episode ParseLink(ParsedRSSFeedLink link)
    {
        if (!link.LinkURL.EndsWith(".mp3"))
            return null;


        return new Episode
        {
            FileUrl = link.LinkURL,
            Permalink = link.LinkURL,
            Summary = link.Description,
            PublicationDate = DateTime.MinValue,
            Title = link.LinkTitle,
            FileType = "audio/mp3",
            Duration = "0:00",
        };
    }
}