using System.Net.Http.Json;
using System.Text.Json;
using AetherGuard.Core.Models;

namespace AetherGuard.Core.Services;

public class AnalysisService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<AnalysisService> _logger;

    public AnalysisService(HttpClient httpClient, ILogger<AnalysisService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<AnalysisResult?> AnalyzeAsync(TelemetryPayload payload)
    {
        try
        {
            using var response = await _httpClient.PostAsJsonAsync("http://ai-service:8000/analyze", payload);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("AI service returned HTTP {StatusCode}", response.StatusCode);
                return null;
            }

            var result = await response.Content.ReadFromJsonAsync<AnalysisResult>(JsonOptions);
            if (result is null)
            {
                _logger.LogWarning("AI service returned an empty response");
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "AI analysis request failed");
            return null;
        }
    }
}
