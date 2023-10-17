namespace JoelRichPodcast.Models;

public class ParsedRSSFeedItem
{
    public string? ItemLink { get; set; }
    public DateTime DateUpdated { get; set; }
    public List<ParsedRSSFeedLink> Links { get; set; } = new();
}