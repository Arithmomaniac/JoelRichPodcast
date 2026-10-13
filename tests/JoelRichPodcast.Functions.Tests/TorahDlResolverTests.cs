using System.Net;
using System.Text;
using JoelRichPodcast.Functions.Models;
using JoelRichPodcast.Functions.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace JoelRichPodcast.Functions.Tests;

public class TorahDlResolverTests
{
    [Fact]
    public async Task ResolveBatchAsync_PreservesFailureReasonAndError()
    {
        const string url = "https://example.test/page";
        const string error = "publisher blocked the request";
        var responseBody = $$"""
            [
              {
                "url": "{{url}}",
                "error": "{{error}}",
                "reason_code": "access_blocked"
              }
            ]
            """;
        var sut = CreateSut(responseBody);

        var result = (await sut.ResolveBatchAsync([url]))[url];

        Assert.NotNull(result);
        Assert.Null(result.DownloadUrl);
        Assert.Equal(ResolutionReasonCodes.AccessBlocked, result.ReasonCode);
        Assert.Equal(error, result.Error);
    }

    [Fact]
    public async Task ResolveBatchAsync_MatchesReorderedResultsByUrl()
    {
        const string firstUrl = "https://example.test/first";
        const string secondUrl = "https://example.test/second";
        var responseBody = $$"""
            [
              {
                "url": "{{secondUrl}}",
                "download_url": "https://media.example.test/second.mp3"
              },
              {
                "url": "{{firstUrl}}",
                "download_url": "https://media.example.test/first.mp3"
              }
            ]
            """;
        var sut = CreateSut(responseBody);

        var results = await sut.ResolveBatchAsync([firstUrl, secondUrl]);

        Assert.Equal("https://media.example.test/first.mp3", results[firstUrl]?.DownloadUrl);
        Assert.Equal("https://media.example.test/second.mp3", results[secondUrl]?.DownloadUrl);
    }

    private static TorahDlResolver CreateSut(string responseBody)
    {
        var handler = new StubHttpMessageHandler(responseBody);
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://resolver.test/") };
        return new TorahDlResolver(new StubHttpClientFactory(client), NullLogger<TorahDlResolver>.Instance);
    }

    private sealed class StubHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StubHttpMessageHandler(string responseBody) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
            return Task.FromResult(response);
        }
    }
}
