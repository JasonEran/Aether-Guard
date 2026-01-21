using System.Text.Json.Serialization;

namespace AetherGuard.Core.Models;

public sealed record AnalysisResult
{
    [JsonPropertyName("status")]
    public string Status { get; init; } = "Unavailable";

    [JsonPropertyName("confidence")]
    public double Confidence { get; init; }

    [JsonPropertyName("prediction")]
    public double Prediction { get; init; }

    [JsonPropertyName("rca")]
    public string RootCause { get; init; } = "Unavailable";
}
