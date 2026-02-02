using AetherGuard.Core.Services.ExternalSignals;
using Xunit;

namespace AetherGuard.Core.Tests;

public class ExternalSignalParserTests
{
    [Fact]
    public void ParsesRssItems()
    {
        var rss = """
                  <?xml version="1.0" encoding="UTF-8"?>
                  <rss version="2.0">
                    <channel>
                      <item>
                        <guid>aws-1</guid>
                        <title>Service disruption in us-east-1</title>
                        <link>https://status.aws.amazon.com/</link>
                        <description>Investigating elevated errors.</description>
                        <pubDate>Fri, 01 Aug 2025 12:00:00 GMT</pubDate>
                      </item>
                    </channel>
                  </rss>
                  """;

        var feed = new ExternalSignalFeedOptions
        {
            Name = "aws-status",
            Url = "https://status.aws.amazon.com/rss/all.rss",
            DefaultRegion = "global"
        };

        var results = ExternalSignalParser.ParseFeed(rss, feed);

        Assert.Single(results);
        Assert.Equal("aws-1", results[0].ExternalId);
        Assert.Equal("aws-status", results[0].Source);
        Assert.Equal("us-east-1", results[0].Region);
    }

    [Fact]
    public void ParsesAtomEntries()
    {
        var atom = """
                   <?xml version="1.0" encoding="UTF-8"?>
                   <feed xmlns="http://www.w3.org/2005/Atom">
                     <entry>
                       <id>tag:status.cloud.google.com,2025:feed:example</id>
                       <title>RESOLVED: incident in us-central1</title>
                       <link href="https://status.cloud.google.com/incident" />
                       <updated>2025-07-23T09:26:58+00:00</updated>
                       <summary>Service recovered.</summary>
                     </entry>
                   </feed>
                   """;

        var feed = new ExternalSignalFeedOptions
        {
            Name = "gcp-status",
            Url = "https://status.cloud.google.com/en/feed.atom",
            DefaultRegion = "global"
        };

        var results = ExternalSignalParser.ParseFeed(atom, feed);

        Assert.Single(results);
        Assert.Equal("gcp-status", results[0].Source);
        Assert.Equal("us-central1", results[0].Region);
        Assert.Equal("incident", results[0].Category);
    }
}
