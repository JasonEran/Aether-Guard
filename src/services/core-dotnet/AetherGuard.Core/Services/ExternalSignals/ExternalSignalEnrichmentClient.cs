using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AetherGuard.Core.Models;
using Microsoft.Extensions.Options;

namespace AetherGuard.Core.Services.ExternalSignals;

public sealed class ExternalSignalEnrichmentClient
{
    private static readonly JsonSerializerOptions RequestJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly JsonSerializerOptions ResponseJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly ExternalSignalEnrichmentOptions _options;
    private readonly ILogger<ExternalSignalEnrichmentClient> _logger;
    private readonly bool _configured;

    public ExternalSignalEnrichmentClient(
        HttpClient httpClient,
        IOptions<ExternalSignalsOptions> options,
        ILogger<ExternalSignalEnrichmentClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        _options = options.Value.Enrichment ?? new ExternalSignalEnrichmentOptions();
        _configured = ConfigureClient(_options);
    }

    public bool IsEnabled => _options.Enabled && _configured;
    public int MaxBatchSize => Math.Clamp(_options.MaxBatchSize, 1, 1000);
    public int MaxConcurrency => Math.Clamp(_options.MaxConcurrency, 1, 8);
    public int SummaryMaxChars => _options.SummaryMaxChars;

    public async Task<SummarizeResponse?> SummarizeAsync(
        IReadOnlyList<ExternalSignal> signals,
        CancellationToken cancellationToken)
    {
        if (!IsEnabled || SummaryMaxChars <= 0 || signals.Count == 0)
        {
            return null;
        }

        var documents = signals.Select(MapDocument).ToList();
        var request = new SummarizeRequestDto(documents, SummaryMaxChars);

        try
        {
            using var response = await _httpClient.PostAsJsonAsync(
                "signals/summarize",
                request,
                RequestJsonOptions,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Summarizer returned HTTP {StatusCode}.", response.StatusCode);
                return null;
            }

            var payload = await response.Content.ReadFromJsonAsync<SummarizeResponseDto>(
                ResponseJsonOptions,
                cancellationToken);

            if (payload?.Summaries is null || payload.Summaries.Count == 0)
            {
                _logger.LogWarning("Summarizer returned an empty payload.");
                return null;
            }

            return new SummarizeResponse(payload.SchemaVersion ?? "unknown", payload.Summaries);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Summarizer request failed.");
            return null;
        }
    }

    public async Task<EnrichResponse?> EnrichAsync(ExternalSignal signal, CancellationToken cancellationToken)
    {
        if (!IsEnabled)
        {
            return null;
        }

        var request = new EnrichRequestDto(new[] { MapDocument(signal) });

        try
        {
            using var response = await _httpClient.PostAsJsonAsync(
                "signals/enrich",
                request,
                RequestJsonOptions,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Enrichment returned HTTP {StatusCode} for signal {ExternalId}.",
                    response.StatusCode,
                    signal.ExternalId);
                return null;
            }

            var payload = await response.Content.ReadFromJsonAsync<EnrichResponseDto>(
                ResponseJsonOptions,
                cancellationToken);

            if (payload is null
                || !TryBuildEnrichResponse(
                    payload.SchemaVersion,
                    payload.SentimentVector,
                    payload.VolatilityProbability,
                    payload.SupplyBias,
                    out var normalized))
            {
                _logger.LogWarning("Enrichment payload missing S_v for signal {ExternalId}.", signal.ExternalId);
                return null;
            }

            return normalized;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Enrichment request failed for signal {ExternalId}.", signal.ExternalId);
            return null;
        }
    }

    public async Task<BatchEnrichResponse?> EnrichBatchAsync(
        IReadOnlyList<ExternalSignal> signals,
        CancellationToken cancellationToken)
    {
        if (!IsEnabled || signals.Count == 0)
        {
            return null;
        }

        var documents = signals.Take(MaxBatchSize).Select(MapDocument).ToList();
        if (documents.Count == 0)
        {
            return null;
        }
        var request = new EnrichRequestDto(documents);

        try
        {
            using var response = await _httpClient.PostAsJsonAsync(
                "signals/enrich/batch",
                request,
                RequestJsonOptions,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Batch enrichment returned HTTP {StatusCode}.", response.StatusCode);
                return null;
            }

            var payload = await response.Content.ReadFromJsonAsync<EnrichBatchResponseDto>(
                ResponseJsonOptions,
                cancellationToken);

            if (payload?.Vectors is null || payload.Vectors.Count == 0)
            {
                _logger.LogWarning("Batch enrichment returned an empty payload.");
                return null;
            }

            var vectors = payload.Vectors
                .Where(item => item.Index >= 0 && item.Index < documents.Count)
                .Select(item =>
                {
                    var isValid = TryBuildEnrichResponse(
                        payload.SchemaVersion,
                        item.SentimentVector,
                        item.VolatilityProbability,
                        item.SupplyBias,
                        out var normalized);
                    return (Index: item.Index, IsValid: isValid, Response: normalized);
                })
                .Where(entry => entry.IsValid && entry.Response is not null)
                .GroupBy(entry => entry.Index)
                .Select(group => new BatchEnrichItem(group.Key, group.First().Response!))
                .ToList();

            if (vectors.Count == 0)
            {
                _logger.LogWarning("Batch enrichment payload had no valid vectors.");
                return null;
            }

            return new BatchEnrichResponse(payload.SchemaVersion ?? "unknown", vectors);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Batch enrichment request failed.");
            return null;
        }
    }

    private bool ConfigureClient(ExternalSignalEnrichmentOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.BaseUrl))
        {
            _logger.LogWarning("External signal enrichment base URL is empty.");
            return false;
        }

        try
        {
            var baseUrl = options.BaseUrl.TrimEnd('/') + "/";
            _httpClient.BaseAddress ??= new Uri(baseUrl, UriKind.Absolute);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Invalid external signal enrichment base URL: {BaseUrl}", options.BaseUrl);
            return false;
        }

        var timeoutSeconds = Math.Clamp(options.TimeoutSeconds, 2, 60);
        _httpClient.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
        return true;
    }

    private static SignalDocumentDto MapDocument(ExternalSignal signal)
        => new(
            Source: signal.Source,
            Title: signal.Title,
            Summary: signal.Summary,
            Url: signal.Url,
            Region: signal.Region,
            PublishedAt: signal.PublishedAt);

    private static bool TryBuildEnrichResponse(
        string? schemaVersion,
        double[]? sentimentVector,
        double volatilityProbability,
        double supplyBias,
        out EnrichResponse? response)
    {
        response = null;
        if (sentimentVector is null || sentimentVector.Length < 3)
        {
            return false;
        }

        var values = sentimentVector.Take(3).ToArray();
        if (values.Any(value => double.IsNaN(value) || double.IsInfinity(value)))
        {
            return false;
        }

        if (double.IsNaN(volatilityProbability) || double.IsInfinity(volatilityProbability))
        {
            return false;
        }

        if (double.IsNaN(supplyBias) || double.IsInfinity(supplyBias))
        {
            return false;
        }

        response = new EnrichResponse(
            schemaVersion ?? "unknown",
            values,
            Math.Clamp(volatilityProbability, 0.0, 1.0),
            Math.Clamp(supplyBias, 0.0, 1.0));
        return true;
    }

    public sealed record SummarizeResponse(string SchemaVersion, IReadOnlyList<SummaryItemDto> Summaries);
    public sealed record EnrichResponse(string SchemaVersion, double[] SentimentVector, double VolatilityProbability, double SupplyBias);
    public sealed record BatchEnrichResponse(string SchemaVersion, IReadOnlyList<BatchEnrichItem> Vectors);
    public sealed record BatchEnrichItem(int Index, EnrichResponse Vector);

    public sealed record SummaryItemDto(
        [property: JsonPropertyName("index")] int Index,
        [property: JsonPropertyName("summary")] string Summary,
        [property: JsonPropertyName("truncated")] bool Truncated);

    private sealed record SignalDocumentDto(
        [property: JsonPropertyName("source")] string Source,
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("summary")] string? Summary,
        [property: JsonPropertyName("url")] string? Url,
        [property: JsonPropertyName("region")] string? Region,
        [property: JsonPropertyName("publishedAt")] DateTimeOffset? PublishedAt);

    private sealed record SummarizeRequestDto(
        [property: JsonPropertyName("documents")] IReadOnlyList<SignalDocumentDto> Documents,
        [property: JsonPropertyName("maxChars")] int? MaxChars);

    private sealed class SummarizeResponseDto
    {
        [JsonPropertyName("schemaVersion")]
        public string? SchemaVersion { get; init; }

        [JsonPropertyName("summaries")]
        public List<SummaryItemDto> Summaries { get; init; } = new();
    }

    private sealed record EnrichRequestDto(
        [property: JsonPropertyName("documents")] IReadOnlyList<SignalDocumentDto> Documents);

    private sealed class EnrichResponseDto
    {
        [JsonPropertyName("schemaVersion")]
        public string? SchemaVersion { get; init; }

        [JsonPropertyName("S_v")]
        public double[]? SentimentVector { get; init; }

        [JsonPropertyName("P_v")]
        public double VolatilityProbability { get; init; }

        [JsonPropertyName("B_s")]
        public double SupplyBias { get; init; }
    }

    private sealed class EnrichBatchResponseDto
    {
        [JsonPropertyName("schemaVersion")]
        public string? SchemaVersion { get; init; }

        [JsonPropertyName("vectors")]
        public List<EnrichBatchVectorDto> Vectors { get; init; } = new();
    }

    private sealed class EnrichBatchVectorDto
    {
        [JsonPropertyName("index")]
        public int Index { get; init; }

        [JsonPropertyName("S_v")]
        public double[]? SentimentVector { get; init; }

        [JsonPropertyName("P_v")]
        public double VolatilityProbability { get; init; }

        [JsonPropertyName("B_s")]
        public double SupplyBias { get; init; }
    }
}
