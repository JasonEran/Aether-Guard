using System.Net.Http.Json;
using System.Text.Json;
using System.Linq;
using AetherGuard.Core.Models;
using Microsoft.Extensions.Configuration;

namespace AetherGuard.Core.Services;

public class AnalysisService
{
    private const int MaxSpotPriceSamples = 200;
    private static readonly JsonSerializerOptions ResponseJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly JsonSerializerOptions RequestJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<AnalysisService> _logger;
    private readonly IReadOnlyList<double> _spotPriceHistory;
    public AnalysisService(
        HttpClient httpClient,
        ILogger<AnalysisService> logger,
        IConfiguration configuration)
    {
        _httpClient = httpClient;
        _logger = logger;
        _spotPriceHistory = LoadSpotPriceHistory(configuration);
    }

    public async Task<AnalysisResult> AnalyzeAsync(TelemetryPayload payload)
    {
        var request = new RiskRequest(
            SpotPriceHistory: _spotPriceHistory,
            RebalanceSignal: payload.RebalanceSignal,
            CapacityScore: ConvertToCapacityScore(payload.DiskAvailable));

        try
        {
            using var response = await _httpClient.PostAsJsonAsync(
                "http://ai-service:8000/analyze",
                request,
                RequestJsonOptions);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("AI service returned HTTP {StatusCode}", response.StatusCode);
                return CreateFallbackResult();
            }

            var result = await response.Content.ReadFromJsonAsync<AnalysisResult>(ResponseJsonOptions);
            if (result is null)
            {
                _logger.LogWarning("AI service returned an empty response");
                return CreateFallbackResult();
            }

            return NormalizePrediction(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI analysis request failed");
            return CreateFallbackResult();
        }
    }

    private static AnalysisResult CreateFallbackResult()
        => new()
        {
            Status = "Unavailable",
            Confidence = 0,
            Prediction = 0,
            RootCause = "Unavailable"
        };

    private static AnalysisResult NormalizePrediction(AnalysisResult result)
        => result with { Prediction = Math.Clamp(result.Prediction, 0, 100) };

    private static double ConvertToCapacityScore(long diskAvailableBytes)
        => diskAvailableBytes / (1024.0 * 1024.0 * 1024.0);

    private IReadOnlyList<double> LoadSpotPriceHistory(IConfiguration configuration)
    {
        var configured = configuration.GetSection("SpotPriceHistory").Get<double[]>() ?? Array.Empty<double>();
        if (configured.Length == 0)
        {
            _logger.LogInformation("SpotPriceHistory is empty; AI volatility analysis will be limited.");
            return Array.Empty<double>();
        }

        var sanitized = configured
            .Where(value => !double.IsNaN(value) && !double.IsInfinity(value) && value >= 0)
            .Take(MaxSpotPriceSamples)
            .ToArray();

        if (sanitized.Length != configured.Length)
        {
            _logger.LogWarning(
                "SpotPriceHistory sanitized from {OriginalCount} to {SanitizedCount} values.",
                configured.Length,
                sanitized.Length);
        }

        return sanitized;
    }

    private sealed record RiskRequest(
        IReadOnlyList<double> SpotPriceHistory,
        bool RebalanceSignal,
        double CapacityScore);
}
