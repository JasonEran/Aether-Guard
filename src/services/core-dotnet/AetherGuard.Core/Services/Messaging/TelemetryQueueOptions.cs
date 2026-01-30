namespace AetherGuard.Core.Services.Messaging;

public sealed record TelemetryQueueOptions
{
    public string QueueName { get; init; } = "telemetry_data";
    public bool EnableDeadLettering { get; init; } = true;
    public string DeadLetterExchange { get; init; } = "telemetry_dlx";
    public string DeadLetterQueueName { get; init; } = "telemetry_data.dlq";
    public string DeadLetterRoutingKey { get; init; } = "telemetry_data.dlq";
    public ushort PrefetchCount { get; init; } = 50;
}
