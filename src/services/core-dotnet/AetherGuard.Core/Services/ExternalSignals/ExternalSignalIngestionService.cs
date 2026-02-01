using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using AetherGuard.Core.Data;
using AetherGuard.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AetherGuard.Core.Services.ExternalSignals;

public sealed class ExternalSignalIngestionService : BackgroundService
{
    private static readonly string[] SeverityKeywords =
    [
        "outage",
        "degraded",
        "disruption",
        "investigating",
        "incident",
        "partial",
        "unavailable",
        "restored",
        "resolved"
    ];

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ExternalSignalIngestionService> _logger;
    private readonly ExternalSignalsOptions _options;

    public ExternalSignalIngestionService(
        IHttpClientFactory httpClientFactory,
        IServiceScopeFactory scopeFactory,
        IOptions<ExternalSignalsOptions> options,
        ILogger<ExternalSignalIngestionService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("External signals ingestion is disabled.");
            return;
        }

        if (_options.Feeds.Count == 0)
        {
            _logger.LogWarning("External signals ingestion enabled but no feeds configured.");
            return;
        }

        var interval = TimeSpan.FromSeconds(Math.Max(60, _options.PollingIntervalSeconds));

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await IngestOnceAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "External signals ingestion cycle failed.");
            }

            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    private async Task IngestOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var httpClient = _httpClientFactory.CreateClient("external-signals");

        var lookback = DateTimeOffset.UtcNow.AddHours(-Math.Max(1, _options.LookbackHours));
        var maxItems = Math.Clamp(_options.MaxItemsPerFeed, 10, 1000);

        foreach (var feed in _options.Feeds)
        {
            if (string.IsNullOrWhiteSpace(feed.Url) || string.IsNullOrWhiteSpace(feed.Name))
            {
                _logger.LogWarning("Skipping external signal feed with missing name/url.");
                continue;
            }

            List<ExternalSignal> parsed;
            try
            {
                var response = await httpClient.GetAsync(feed.Url, cancellationToken);
                response.EnsureSuccessStatusCode();
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                parsed = ParseFeed(content, feed);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch external signal feed {Feed}.", feed.Name);
                continue;
            }

            if (parsed.Count == 0)
            {
                continue;
            }

            parsed = parsed
                .Where(signal => signal.PublishedAt >= lookback)
                .OrderByDescending(signal => signal.PublishedAt)
                .Take(maxItems)
                .ToList();

            if (parsed.Count == 0)
            {
                continue;
            }

            var existingIds = await db.ExternalSignals
                .Where(signal => signal.Source == feed.Name && signal.PublishedAt >= lookback)
                .Select(signal => signal.ExternalId)
                .ToListAsync(cancellationToken);

            var existing = existingIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var newSignals = parsed
                .Where(signal => !existing.Contains(signal.ExternalId))
                .GroupBy(signal => signal.ExternalId, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();

            if (newSignals.Count == 0)
            {
                continue;
            }

            db.ExternalSignals.AddRange(newSignals);
            await db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation("Ingested {Count} signals from {Feed}.", newSignals.Count, feed.Name);
        }
    }

    private static List<ExternalSignal> ParseFeed(string content, ExternalSignalFeedOptions feed)
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

            var severity = GuessSeverity(title, summary);
            var category = GuessCategory(title, summary);
            var region = ExtractRegion(title, summary) ?? feed.DefaultRegion;

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

    private static string? GuessSeverity(string title, string? summary)
    {
        var content = $"{title} {summary}".ToLowerInvariant();
        foreach (var keyword in SeverityKeywords)
        {
            if (content.Contains(keyword, StringComparison.OrdinalIgnoreCase))
            {
                return keyword;
            }
        }

        return "info";
    }

    private static string? GuessCategory(string title, string? summary)
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
        var match = System.Text.RegularExpressions.Regex.Match(content, @"\b([a-z]{2}-[a-z]+-\d)\b", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (match.Success)
        {
            return match.Groups[1].Value;
        }

        return null;
    }

    private static string? BuildTags(string title, string? summary, string? severity, string? category)
    {
        var tags = new List<string>();
        if (!string.IsNullOrWhiteSpace(severity))
        {
            tags.Add(severity);
        }

        if (!string.IsNullOrWhiteSpace(category) && !tags.Any(tag => string.Equals(tag, category, StringComparison.OrdinalIgnoreCase)))
        {
            tags.Add(category);
        }

        if (title.Contains("region", StringComparison.OrdinalIgnoreCase))
        {
            tags.Add("region");
        }

        if (!string.IsNullOrWhiteSpace(summary) && summary.Contains("latency", StringComparison.OrdinalIgnoreCase))
        {
            tags.Add("latency");
        }

        return tags.Count > 0 ? string.Join(",", tags) : null;
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
