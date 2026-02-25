using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using AetherGuard.Core.Models;

namespace AetherGuard.Core.Services.ExternalSignals;

public static class ExternalSignalParser
{
    private static readonly string[] CriticalKeywords = ["outage", "disruption", "unavailable"];
    private static readonly string[] WarningKeywords = ["degraded", "incident", "investigating", "partial"];
    private static readonly string[] InfoKeywords = ["maintenance", "resolved", "restored", "notice"];

    public static List<ExternalSignal> ParseFeed(string content, ExternalSignalFeedOptions feed)
    {
        var document = XDocument.Parse(content);
        var root = document.Root;
        if (root is null)
        {
            return [];
        }

        var items = root.Name.LocalName switch
        {
            "rss" => root.Descendants().Where(e => e.Name.LocalName == "item"),
            "feed" => root.Descendants().Where(e => e.Name.LocalName == "entry"),
            _ => root.Descendants().Where(e => e.Name.LocalName == "item" || e.Name.LocalName == "entry")
        };

        var signals = new List<ExternalSignal>();
        foreach (var item in items)
        {
            var title = GetValue(item, "title");
            if (string.IsNullOrWhiteSpace(title))
            {
                continue;
            }

            var externalId = GetValue(item, "guid") ?? GetValue(item, "id") ?? title;
            var url = GetValue(item, "link");
            if (string.IsNullOrWhiteSpace(url))
            {
                url = GetLinkHref(item);
            }

            var summaryRaw = GetValue(item, "description") ?? GetValue(item, "summary") ?? GetValue(item, "content");
            var summary = NormalizeText(summaryRaw);
            var published = ParseDate(GetValue(item, "pubDate"))
                ?? ParseDate(GetValue(item, "updated"))
                ?? ParseDate(GetValue(item, "published"))
                ?? DateTimeOffset.UtcNow;

            var severity = NormalizeSeverity(title, summary);
            var category = NormalizeCategory(title, summary);
            var region = NormalizeRegion(ExtractRegion(title, summary) ?? feed.DefaultRegion);

            signals.Add(new ExternalSignal
            {
                Source = feed.Name,
                ExternalId = externalId,
                Title = title,
                Summary = summary,
                Region = region,
                Severity = severity,
                Category = category,
                Url = url,
                Tags = BuildTags(title, summary, severity, category),
                PublishedAt = published,
                IngestedAt = DateTimeOffset.UtcNow
            });
        }

        return signals;
    }

    private static string? GetValue(XElement parent, string name)
    {
        return parent.Elements().FirstOrDefault(e => e.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value;
    }

    private static string? GetLinkHref(XElement parent)
    {
        var link = parent.Elements().FirstOrDefault(e => e.Name.LocalName.Equals("link", StringComparison.OrdinalIgnoreCase));
        return link?.Attribute("href")?.Value;
    }

    private static DateTimeOffset? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    private static string? NormalizeSeverity(string title, string? summary)
    {
        var content = $"{title} {summary}".ToLowerInvariant();
        if (CriticalKeywords.Any(keyword => content.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
        {
            return "critical";
        }

        if (content.Contains("resolved", StringComparison.OrdinalIgnoreCase)
            || content.Contains("restored", StringComparison.OrdinalIgnoreCase))
        {
            return "info";
        }

        if (WarningKeywords.Any(keyword => content.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
        {
            return "warning";
        }

        if (InfoKeywords.Any(keyword => content.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
        {
            return "info";
        }

        return "info";
    }

    private static string? NormalizeCategory(string title, string? summary)
    {
        var content = $"{title} {summary}".ToLowerInvariant();
        if (content.Contains("maintenance"))
        {
            return "maintenance";
        }
        if (content.Contains("outage") || content.Contains("disruption"))
        {
            return "outage";
        }
        if (content.Contains("degraded"))
        {
            return "degraded";
        }
        if (content.Contains("incident") || content.Contains("investigating"))
        {
            return "incident";
        }

        return "notice";
    }

    private static string? ExtractRegion(string title, string? summary)
    {
        var content = $"{title} {summary}";
        var match = Regex.Match(
            content,
            @"\b([a-z]{2}-[a-z]+-\d|[a-z]{2}-[a-z]+[0-9])\b",
            RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string? BuildTags(string title, string? summary, string? severity, string? category)
    {
        var tags = new List<string>();
        if (!string.IsNullOrWhiteSpace(severity))
        {
            tags.Add(severity.ToLowerInvariant());
        }

        if (!string.IsNullOrWhiteSpace(category) && !tags.Any(tag => string.Equals(tag, category, StringComparison.OrdinalIgnoreCase)))
        {
            tags.Add(category.ToLowerInvariant());
        }

        if (title.Contains("region", StringComparison.OrdinalIgnoreCase))
        {
            tags.Add("region");
        }

        if (!string.IsNullOrWhiteSpace(summary) && summary.Contains("latency", StringComparison.OrdinalIgnoreCase))
        {
            tags.Add("latency");
        }

        return tags.Count > 0 ? string.Join(",", tags.Distinct(StringComparer.OrdinalIgnoreCase)) : null;
    }

    private static string? NormalizeRegion(string? region)
    {
        if (string.IsNullOrWhiteSpace(region))
        {
            return null;
        }

        return region.Trim().ToLowerInvariant();
    }

    private static string? NormalizeText(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return raw;
        }

        var decoded = WebUtility.HtmlDecode(raw);
        var withoutTags = Regex.Replace(decoded, "<.*?>", string.Empty, RegexOptions.Singleline);
        return withoutTags.Trim();
    }
}
