namespace AetherGuard.Core.Models;

public sealed class ExternalSignal
{
    public long Id { get; set; }
    public string Source { get; set; } = string.Empty;
    public string ExternalId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public string? SummaryDigest { get; set; }
    public bool? SummaryDigestTruncated { get; set; }
    public string? SummarySchemaVersion { get; set; }
    public string? EnrichmentSchemaVersion { get; set; }
    public double? SentimentNegative { get; set; }
    public double? SentimentNeutral { get; set; }
    public double? SentimentPositive { get; set; }
    public double? VolatilityProbability { get; set; }
    public double? SupplyBias { get; set; }
    public DateTimeOffset? SummarizedAt { get; set; }
    public DateTimeOffset? EnrichedAt { get; set; }
    public string? Region { get; set; }
    public string? Severity { get; set; }
    public string? Category { get; set; }
    public string? Url { get; set; }
    public string? Tags { get; set; }
    public DateTimeOffset PublishedAt { get; set; }
    public DateTimeOffset IngestedAt { get; set; }
}
