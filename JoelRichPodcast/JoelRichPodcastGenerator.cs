using System.Xml;
using System.IO;
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

    public string GetPodcastXml()
    {
        var pg = _podcastGeneratorFactory.GetPodcastGenerator();

        using var ms = new MemoryStream();
        using (var xmlTextWriter = new XmlTextWriter(ms, Encoding.UTF8))
        {
            pg.Generate(xmlTextWriter);
        }

        var xml = Encoding.UTF8.GetString(ms.ToArray());
        return xml;
    }
}

