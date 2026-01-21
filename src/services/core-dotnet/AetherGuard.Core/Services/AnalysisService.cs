using System.Net.Http.Json;
using System.Text.Json;
using AetherGuard.Core.Data;
using AetherGuard.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace AetherGuard.Core.Services;

public class AnalysisService
{
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
    private readonly ApplicationDbContext _context;

    public AnalysisService(
        HttpClient httpClient,
        ILogger<AnalysisService> logger,
        ApplicationDbContext context)
    {
        _httpClient = httpClient;
        _logger = logger;
        _context = context;
    }

    public async Task<AnalysisResult> AnalyzeAsync(TelemetryPayload payload)
    {
        var history = await _context.TelemetryRecords
            .AsNoTracking()
            .Where(record => record.AgentId == payload.AgentId)
            .OrderByDescending(record => record.Timestamp)
            .Take(9)
            .ToListAsync();

        if (history.Count < 9)
        {
            _logger.LogInformation("Insufficient telemetry history for agent {AgentId}", payload.AgentId);
            return CreateFallbackResult();
        }

        history.Reverse();
        var sequence = history
            .Select(record => new TelemetrySample(record.CpuUsage, record.MemoryUsage))
            .ToList();
        sequence.Add(new TelemetrySample(payload.CpuUsage, payload.MemoryUsage));

        try
        {
            using var response = await _httpClient.PostAsJsonAsync(
                "http://ai-service:8000/analyze",
                sequence,
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

            return result;
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

    private sealed record TelemetrySample(double CpuUsage, double MemoryUsage);
}
