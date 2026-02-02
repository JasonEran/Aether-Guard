using AetherGuard.Core.Data;
using AetherGuard.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AetherGuard.Core.Services.ExternalSignals;

public sealed class ExternalSignalIngestionService : BackgroundService
{
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
                var statusCode = (int)response.StatusCode;
                response.EnsureSuccessStatusCode();
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                parsed = ExternalSignalParser.ParseFeed(content, feed);
                await UpdateFeedStateAsync(db, feed, statusCode, null, cancellationToken);
            }
            catch (Exception ex)
            {
                await UpdateFeedStateAsync(db, feed, null, ex.Message, cancellationToken);
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

    private static async Task UpdateFeedStateAsync(
        ApplicationDbContext db,
        ExternalSignalFeedOptions feed,
        int? statusCode,
        string? error,
        CancellationToken cancellationToken)
    {
        var state = await db.ExternalSignalFeedStates
            .FirstOrDefaultAsync(existing => existing.Name == feed.Name, cancellationToken);

        if (state is null)
        {
            state = new ExternalSignalFeedState
            {
                Name = feed.Name,
                Url = feed.Url
            };
            db.ExternalSignalFeedStates.Add(state);
        }

        state.Url = feed.Url;
        state.LastFetchAt = DateTimeOffset.UtcNow;
        state.LastStatusCode = statusCode;

        if (string.IsNullOrWhiteSpace(error))
        {
            state.LastSuccessAt = state.LastFetchAt;
            state.LastError = null;
            state.FailureCount = 0;
        }
        else
        {
            state.LastError = error.Length > 1000 ? error[..1000] : error;
            state.FailureCount += 1;
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
