namespace AetherGuard.Core.Services.ExternalSignals;

public sealed class ExternalSignalsOptions
{
    public bool Enabled { get; set; } = false;
    public int PollingIntervalSeconds { get; set; } = 300;
    public int LookbackHours { get; set; } = 48;
    public int MaxItemsPerFeed { get; set; } = 200;
    public int RetentionDays { get; set; } = 30;
    public int CleanupBatchSize { get; set; } = 500;
    public List<ExternalSignalFeedOptions> Feeds { get; set; } = new();
    public ExternalSignalEnrichmentOptions Enrichment { get; set; } = new();
}

public sealed class ExternalSignalFeedOptions
{
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string? DefaultRegion { get; set; }
}

public sealed class ExternalSignalEnrichmentOptions
{
    public bool Enabled { get; set; } = true;
    public string BaseUrl { get; set; } = "http://ai-service:8000";
    public int TimeoutSeconds { get; set; } = 8;
    public int MaxBatchSize { get; set; } = 200;
    public int MaxConcurrency { get; set; } = 4;
    public int SummaryMaxChars { get; set; } = 280;
}
