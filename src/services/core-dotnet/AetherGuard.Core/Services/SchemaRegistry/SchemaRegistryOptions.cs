namespace AetherGuard.Core.Services.SchemaRegistry;

public sealed record SchemaRegistryOptions
{
    public bool Enabled { get; init; } = true;
    public string Compatibility { get; init; } = "BACKWARD";
}
