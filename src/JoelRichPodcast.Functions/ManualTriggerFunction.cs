using JoelRichPodcast.Functions.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace JoelRichPodcast.Functions;

/// <summary>
/// HTTP-triggered version for local testing and manual runs.
/// GET /api/scrape — runs the full scrape-and-publish pipeline on demand.
/// </summary>
public class ManualTriggerFunction(
    PodcastPipeline pipeline,
    ILogger<ManualTriggerFunction> logger)
{
    [Function("ManualScrape")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Function, "get", Route = "scrape")] HttpRequestData req)
    {
        logger.LogInformation("Manual scrape triggered");

        try
        {
            var feedXml = await pipeline.RunAsync();

            var response = req.CreateResponse(System.Net.HttpStatusCode.OK);
            response.Headers.Add("Content-Type", "application/rss+xml; charset=utf-8");
            await response.WriteStringAsync(feedXml);
            return response;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ManualScrape failed");
            var errorResponse = req.CreateResponse(System.Net.HttpStatusCode.InternalServerError);
            errorResponse.Headers.Add("Content-Type", "text/plain; charset=utf-8");
            await errorResponse.WriteStringAsync("Manual scrape failed. Check Application Insights logs.");
            return errorResponse;
        }
    }
}
