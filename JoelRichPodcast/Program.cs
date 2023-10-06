using JoelRichPodcast.Services;
using JoelRichPodcast.Abstract;
using JoelRichPodcast.Decorators;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;

namespace JoelRichPodcast;

internal class Program
{

    public static async Task Main(string[] args)
    {
        var hostBuilder = Host.CreateDefaultBuilder()
            .ConfigureServices(services => services
            .AddSingleton<JoelRichPodcastGenerator>()
            .AddSingleton<JoelRichFeedGenerator>()
            .AddSingleton<PodcastGeneratorFactory>()
            .AddScoped<ILinkParser, YUTorahLinkParser>()
            .AddScoped<ILinkParser, MP3LinkParser>()
            .Decorate<ILinkParser, LoggedLinkedParser>());

        using var host = hostBuilder.Build();
        host.Services.GetService<JoelRichPodcastGenerator>().GetPodcastXml();
    }
}

