using JoelRichPodcast.Services;
using JoelRichPodcast.Abstract;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace JoelRichPodcast;

internal class Program
{

    public static async Task Main(string[] args)
    {
        var hostBuilder = Host.CreateDefaultBuilder()
            .ConfigureServices(services => services
                .AddHttpClient()
                .AddSingleton<JoelRichPodcastGenerator>()
                .AddSingleton<JoelRichFeedGenerator>()
                .AddSingleton<PodcastGeneratorFactory>()
                .AddScoped<ILinkParser, MP3LinkParser>()
                .AddScoped<ILinkParser, TorahAnytimeParser>()
                .AddScoped<ILinkParser, YUTorahLinkParser>()
                .AddScoped<ILinkParser, TorahInMotionLinkParser>()
                .AddScoped<ILinkParser, PodbeanLinkParser>()
        );

        using var host = hostBuilder.Build();

        await host.Services.GetRequiredService<JoelRichPodcastGenerator>().GetPodcastXml();
    }
}

