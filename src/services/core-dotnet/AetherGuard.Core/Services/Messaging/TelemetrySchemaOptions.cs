namespace AetherGuard.Core.Services.Messaging;

public sealed record TelemetrySchemaOptions
{
    public int CurrentVersion { get; init; } = 1;
    public int MinSupportedVersion { get; init; } = 1;
    public int MaxSupportedVersion { get; init; } = 1;
    public string OnUnsupported { get; init; } = "drop";
}
