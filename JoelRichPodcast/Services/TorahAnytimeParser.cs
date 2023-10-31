using JoelRichPodcast.Abstract;
using JoelRichPodcast.Models;
using Microsoft.Extensions.Configuration;
using PodcastRssGenerator4DotNet;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace JoelRichPodcast.Services;

internal partial class TorahAnytimeParser : ILinkParser
{
    private IHttpClientFactory httpClientFactory;
    private readonly IConfiguration configuration;

    public TorahAnytimeParser(IHttpClientFactory httpClientFactory, IConfiguration configuration)
    {
        this.httpClientFactory = httpClientFactory;
        this.configuration = configuration;
    }

    static readonly Regex urlRegex = MyRegex();

    public async Task<Episode?> ParseLink(ParsedRSSFeedLink link)
    {
        var match = urlRegex.Match(link.LinkURL);
        if (!match.Success)
        {
            return null;
        }
        var numericPart = match.Groups[1].Value;
        var httpClient = httpClientFactory.CreateClient();
        var content = new FormUrlEncodedContent(new KeyValuePair<string, string>[]
        {
            new("uniqid", configuration["TorahAnytimeUniqId"]!),
            new("v", numericPart)
        });
        var response = await httpClient.PostAsync("https://www.torahanytime.com/u/download", content);
        response.EnsureSuccessStatusCode();
        var responseUri = (await response.Content.ReadFromJsonAsync<JsonNode>())!["link"]!.GetValue<string>();

        responseUri = responseUri.Replace("https://dl.torahanytime.com/audio/", "https://www.torahanytime.com/dl/audio/");

        return new Episode
        {
            FileUrl = responseUri,
            Permalink = link.LinkURL,
            Summary = link.Description,
            PublicationDate = DateTime.MinValue,
            Title = link.LinkTitle,
            FileType = "audio/mp3",
            Duration = "0:00"
        };
    }

    [GeneratedRegex("https:\\/\\/www\\.torahanytime\\.com\\/#\\S+\\?[va]=(\\d+)")]
    private static partial Regex MyRegex();
}
