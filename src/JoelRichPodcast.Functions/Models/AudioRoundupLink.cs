namespace JoelRichPodcast.Functions.Models;

public record AudioRoundupLink(
    string Description,
    string LinkTitle,
    string LinkUrl,
    DateTimeOffset PublishDate,
    string RoundupUrl,
    int RoundupIndex);
