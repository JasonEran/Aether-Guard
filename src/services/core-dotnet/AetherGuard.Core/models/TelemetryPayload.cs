namespace AetherGuard.Core.Models;

public record TelemetryPayload(
    string AgentId,
    long Timestamp,
    double CpuUsage,
    double MemoryUsage,
    double DiskIoUsage,
    string Metadata 
);