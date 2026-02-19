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

            if (_options.Enrichment.Enabled)
            {
                var enrichmentClient = scope.ServiceProvider.GetRequiredService<ExternalSignalEnrichmentClient>();
                await EnrichSignalsAsync(db, enrichmentClient, newSignals, cancellationToken);
            }
        }

        await CleanupOldSignalsAsync(db, cancellationToken);
    }

    private async Task EnrichSignalsAsync(
        ApplicationDbContext db,
        ExternalSignalEnrichmentClient client,
        List<ExternalSignal> signals,
        CancellationToken cancellationToken)
    {
        if (!client.IsEnabled || signals.Count == 0)
        {
            return;
        }

        var batch = signals.Take(client.MaxBatchSize).ToList();
        if (batch.Count == 0)
        {
            return;
        }

        var summaryUpdates = 0;
        var enrichmentUpdates = 0;
        var summarizedAt = DateTimeOffset.UtcNow;

        var summaryResponse = await client.SummarizeAsync(batch, cancellationToken);
        if (summaryResponse is not null)
        {
            var summaryByIndex = summaryResponse.Summaries
                .Where(item => item.Index >= 0 && item.Index < batch.Count)
                .GroupBy(item => item.Index)
                .ToDictionary(group => group.Key, group => group.First());

            for (var index = 0; index < batch.Count; index++)
            {
                if (!summaryByIndex.TryGetValue(index, out var item))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(item.Summary))
                {
                    continue;
                }

                var signal = batch[index];
                signal.SummaryDigest = item.Summary;
                signal.SummaryDigestTruncated = item.Truncated;
                signal.SummarySchemaVersion = summaryResponse.SchemaVersion;
                signal.SummarizedAt = summarizedAt;
                summaryUpdates++;
            }
        }

        var enrichedAt = DateTimeOffset.UtcNow;
        var batchResponse = await client.EnrichBatchAsync(batch, cancellationToken);

        if (batchResponse is not null)
        {
            var vectorsByIndex = batchResponse.Vectors
                .Where(item => item.Index >= 0 && item.Index < batch.Count)
                .GroupBy(item => item.Index)
                .ToDictionary(group => group.Key, group => group.First().Vector);

            for (var index = 0; index < batch.Count; index++)
            {
                if (!vectorsByIndex.TryGetValue(index, out var vector))
                {
                    continue;
                }

                if (vector.SentimentVector.Length < 3)
                {
                    continue;
                }

                ApplyEnrichment(batch[index], vector, enrichedAt);
                enrichmentUpdates++;
            }
        }
        else
        {
            var semaphore = new SemaphoreSlim(client.MaxConcurrency);
            var tasks = batch.Select(async signal =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    var result = await client.EnrichAsync(signal, cancellationToken);
                    return (signal, result);
                }
                finally
                {
                    semaphore.Release();
                }
            }).ToList();

            var results = await Task.WhenAll(tasks);
            foreach (var (signal, result) in results)
            {
                if (result is null || result.SentimentVector.Length < 3)
                {
                    continue;
                }

                ApplyEnrichment(signal, result, enrichedAt);
                enrichmentUpdates++;
            }
        }

        if (summaryUpdates > 0 || enrichmentUpdates > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
            _logger.LogInformation(
                "Enriched external signals: summaries={SummaryCount}, vectors={VectorCount}.",
                summaryUpdates,
                enrichmentUpdates);
        }
    }

    private static double Clamp01(double value)
        => Math.Clamp(value, 0.0, 1.0);

    private static void ApplyEnrichment(
        ExternalSignal signal,
        ExternalSignalEnrichmentClient.EnrichResponse result,
        DateTimeOffset enrichedAt)
    {
        signal.SentimentNegative = Clamp01(result.SentimentVector[0]);
        signal.SentimentNeutral = Clamp01(result.SentimentVector[1]);
        signal.SentimentPositive = Clamp01(result.SentimentVector[2]);
        signal.VolatilityProbability = Clamp01(result.VolatilityProbability);
        signal.SupplyBias = Clamp01(result.SupplyBias);
        signal.EnrichmentSchemaVersion = result.SchemaVersion;
        signal.EnrichedAt = enrichedAt;
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

    private async Task CleanupOldSignalsAsync(ApplicationDbContext db, CancellationToken cancellationToken)
    {
        if (_options.RetentionDays <= 0)
        {
            return;
        }

        var cutoff = DateTimeOffset.UtcNow.AddDays(-_options.RetentionDays);
        var batchSize = Math.Clamp(_options.CleanupBatchSize, 50, 2000);
        var totalRemoved = 0;

        while (true)
        {
            var ids = await db.ExternalSignals
                .AsNoTracking()
                .Where(signal => signal.PublishedAt < cutoff)
                .OrderBy(signal => signal.PublishedAt)
                .Select(signal => signal.Id)
                .Take(batchSize)
                .ToListAsync(cancellationToken);

            if (ids.Count == 0)
            {
                break;
            }

            var toRemove = ids.Select(id => new ExternalSignal { Id = id }).ToList();
            db.ExternalSignals.RemoveRange(toRemove);
            var removed = await db.SaveChangesAsync(cancellationToken);
            totalRemoved += removed;

            if (ids.Count < batchSize)
            {
                break;
            }
        }

        if (totalRemoved > 0)
        {
            _logger.LogInformation("Removed {Count} external signals older than {Cutoff}.", totalRemoved, cutoff);
        }
    }
}
