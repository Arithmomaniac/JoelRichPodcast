using JoelRichPodcast.Functions.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace JoelRichPodcast.Functions;

public class ScrapeAndPublishFunction(
    PodcastPipeline pipeline,
    ILogger<ScrapeAndPublishFunction> logger)
{
    [Function("ScrapeAndPublish")]
    public async Task Run([TimerTrigger("0 0 */6 * * *")] TimerInfo timerInfo)
    {
        logger.LogInformation("ScrapeAndPublish triggered at {Time}", DateTimeOffset.UtcNow);
        await pipeline.RunAsync();
    }
}
