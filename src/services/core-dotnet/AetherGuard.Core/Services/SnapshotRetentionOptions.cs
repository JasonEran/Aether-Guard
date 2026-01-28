namespace AetherGuard.Core.Services;

public sealed record SnapshotRetentionOptions
{
    public bool Enabled { get; init; } = true;
    public int SweepIntervalMinutes { get; init; } = 60;
    public int MaxAgeDays { get; init; } = 14;
    public int MaxSnapshotsPerWorkload { get; init; } = 5;
    public int MaxTotalSnapshots { get; init; } = 500;
    public int ScanLimit { get; init; } = 2000;
    public bool ApplyS3Lifecycle { get; init; } = true;
    public int S3ExpirationDays { get; init; } = 30;
}
