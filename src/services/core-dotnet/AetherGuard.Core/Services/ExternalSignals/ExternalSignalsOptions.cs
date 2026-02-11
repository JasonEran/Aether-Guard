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
}

public sealed class ExternalSignalFeedOptions
{
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string? DefaultRegion { get; set; }
}
