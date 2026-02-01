namespace AetherGuard.Core.Models;

public sealed class ExternalSignal
{
    public long Id { get; set; }
    public string Source { get; set; } = string.Empty;
    public string ExternalId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? Summary { get; set; }
    public string? Region { get; set; }
    public string? Severity { get; set; }
    public string? Category { get; set; }
    public string? Url { get; set; }
    public string? Tags { get; set; }
    public DateTimeOffset PublishedAt { get; set; }
    public DateTimeOffset IngestedAt { get; set; }
}
