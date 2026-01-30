using AetherGuard.Core.Models;

namespace AetherGuard.Core.Services.Messaging;

public sealed record TelemetryEnvelope(
    int SchemaVersion,
    long SentAt,
    TelemetryPayload Payload);
