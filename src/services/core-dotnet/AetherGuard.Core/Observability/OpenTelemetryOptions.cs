namespace AetherGuard.Core.Observability;

public sealed record OpenTelemetryOptions
{
    public bool Enabled { get; init; }
    public string ServiceName { get; init; } = "aether-guard-core";
    public string OtlpEndpoint { get; init; } = "http://otel-collector:4317";
    public string Protocol { get; init; } = "grpc";
}
