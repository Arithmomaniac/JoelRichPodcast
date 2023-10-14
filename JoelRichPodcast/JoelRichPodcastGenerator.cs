using System.Xml;
using System.Text;
using JoelRichPodcast.Services;

namespace JoelRichPodcast;

public class JoelRichPodcastGenerator
{
    private readonly PodcastGeneratorFactory _podcastGeneratorFactory;

    public JoelRichPodcastGenerator(PodcastGeneratorFactory podcastGeneratorFactory)
    {
        _podcastGeneratorFactory = podcastGeneratorFactory;
    }

    public async Task<string> GetPodcastXml()
    {
        var pg = await _podcastGeneratorFactory.GetPodcastGenerator();

        using var ms = new MemoryStream();
        using (var xmlTextWriter = new XmlTextWriter(ms, Encoding.UTF8))
        {
            pg.Generate(xmlTextWriter);
        }

        var xml = Encoding.UTF8.GetString(ms.ToArray());
        return xml;
    }
}

