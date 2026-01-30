namespace AetherGuard.Core.Models;

public sealed class SchemaRegistryEntry
{
    public long Id { get; set; }
    public string Subject { get; set; } = string.Empty;
    public int Version { get; set; }
    public string Schema { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
