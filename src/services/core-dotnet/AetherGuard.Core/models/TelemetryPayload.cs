namespace AetherGuard.Core.Models;

public record TelemetryPayload(
    string AgentId,
    long Timestamp,
    string WorkloadTier,
    bool RebalanceSignal,
    long DiskAvailable
);
