using System;
using System.Xml;
using StructureMap;
using Common.Logging;
using System.IO;
using System.Text;
using JoelRichPodcast.Services;
using JoelRichPodcast.Abstract;
using JoelRichPodcast.Decorators;

namespace JoelRichPodcast;

internal class Program
{

    private static void Main(string[] args)
    {
        using (var container = GetContainer())
        {
            var pg = container.GetInstance<PodcastGeneratorFactory>().GetPodcastGenerator();

            using var ms = new MemoryStream();
            using (var xmlTextWriter = new XmlTextWriter(ms, Encoding.UTF8))
            {
                pg.Generate(xmlTextWriter);
            }

            var xml = Encoding.UTF8.GetString(ms.ToArray());

            return;
        }
        Console.Write("Program Complete.");
        Console.ReadLine();
    }

    private static IContainer GetContainer()
    {
        return new Container(c =>
        {
            c.For<ILog>().Use(LogManager.GetLogger<Program>());

            c.For<ILinkParser>().DecorateAllWith<LoggedLinkedParser>();
            c.For<ILinkParser>().Add<YUTorahLinkParser>();
            c.For<ILinkParser>().Add<MP3LinkParser>();
        });
    }
}

