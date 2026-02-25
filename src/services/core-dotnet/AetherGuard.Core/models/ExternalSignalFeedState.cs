namespace AetherGuard.Core.Models;

public sealed class ExternalSignalFeedState
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public DateTimeOffset LastFetchAt { get; set; }
    public DateTimeOffset? LastSuccessAt { get; set; }
    public int FailureCount { get; set; }
    public string? LastError { get; set; }
    public int? LastStatusCode { get; set; }
}
