using Azure;
using Azure.Data.Tables;

namespace JoelRichPodcast.Functions.Models;

public class EpisodeEntity : ITableEntity
{
    public string PartitionKey { get; set; } = "roundup";
    public string RowKey { get; set; } = string.Empty;
    public DateTimeOffset? Timestamp { get; set; }
    public ETag ETag { get; set; }

    public string? Title { get; set; }
    public DateTimeOffset? PublishDate { get; set; }
    public string? AudioUrl { get; set; }
    public string? AudioContentType { get; set; }
    public long? AudioContentLength { get; set; }
    public string? SourceUrl { get; set; }
    public string? Description { get; set; }
    public string? RoundupUrl { get; set; }
    public int? RoundupIndex { get; set; }

    public static string MakeRowKey(DateTimeOffset date, string title)
    {
        var slug = Slugify(title);
        return $"{date:yyyy-MM-dd}_{slug}";
    }

    private static string Slugify(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return "untitled";

        var slug = text.ToLowerInvariant();
        slug = System.Text.RegularExpressions.Regex.Replace(slug, @"[^a-z0-9\s-]", "");
        slug = System.Text.RegularExpressions.Regex.Replace(slug, @"\s+", "-");
        slug = slug.Trim('-');

        return slug.Length > 80 ? slug[..80].TrimEnd('-') : slug;
    }
}
