using AetherGuard.Core.Models;
using AetherGuard.Core.Services.Messaging;
using AetherGuard.Grpc.V1;
using Microsoft.AspNetCore.Http;
using StackExchange.Redis;

namespace AetherGuard.Core.Services;

public sealed record IngestionOutcome(TelemetryAck Ack, bool IsDuplicate, DateTimeOffset EnqueuedAt);

public class TelemetryIngestionService
{
    private readonly ILogger<TelemetryIngestionService> _logger;
    private readonly IConnectionMultiplexer _redis;
    private readonly IMessageProducer _producer;

    public TelemetryIngestionService(
        ILogger<TelemetryIngestionService> logger,
        IConnectionMultiplexer redis,
        IMessageProducer producer)
    {
        _logger = logger;
        _redis = redis;
        _producer = producer;
    }

    public async Task<ApiResult<IngestionOutcome>> IngestAsync(
        AetherGuard.Grpc.V1.TelemetryRecord record,
        CancellationToken cancellationToken)
    {
        if (record is null)
        {
            return ApiResult<IngestionOutcome>.Fail(
                StatusCodes.Status400BadRequest,
                "Telemetry payload is required.",
                "Telemetry payload is required.");
        }

        if (string.IsNullOrWhiteSpace(record.AgentId))
        {
            return ApiResult<IngestionOutcome>.Fail(
                StatusCodes.Status400BadRequest,
                "AgentId is required.",
                "AgentId is required.");
        }

        if (record.Timestamp <= 0)
        {
            return ApiResult<IngestionOutcome>.Fail(
                StatusCodes.Status400BadRequest,
                "Timestamp is required.",
                "Timestamp is required.");
        }

        if (string.IsNullOrWhiteSpace(record.WorkloadTier))
        {
            return ApiResult<IngestionOutcome>.Fail(
                StatusCodes.Status400BadRequest,
                "WorkloadTier is required.",
                "WorkloadTier is required.");
        }

        var tier = record.WorkloadTier.Trim().ToUpperInvariant();
        if (tier is not ("T1" or "T2" or "T3"))
        {
            _logger.LogWarning("Received invalid tier data from {AgentId}", record.AgentId);
            return ApiResult<IngestionOutcome>.Fail(
                StatusCodes.Status400BadRequest,
                "Invalid WorkloadTier value.",
                "Invalid WorkloadTier value.");
        }

        if (record.DiskAvailable < 0)
        {
            _logger.LogWarning("Received invalid disk data from {AgentId}", record.AgentId);
            return ApiResult<IngestionOutcome>.Fail(
                StatusCodes.Status400BadRequest,
                "Invalid DiskAvailable value.",
                "Invalid DiskAvailable value.");
        }

        var dedupKey = $"dedup:{record.AgentId}:{record.Timestamp}";
        var database = _redis.GetDatabase();
        var setResult = await database.StringSetAsync(
            dedupKey,
            "1",
            TimeSpan.FromSeconds(10),
            When.NotExists);

        var now = DateTimeOffset.UtcNow;
        var ack = new TelemetryAck
        {
            Status = setResult ? "queued" : "duplicate",
            Timestamp = now.ToUnixTimeSeconds()
        };
        var outcome = new IngestionOutcome(ack, !setResult, now);

        if (!setResult)
        {
            _logger.LogInformation("[Telemetry] Duplicate payload ignored for {AgentId}", record.AgentId);
            return ApiResult<IngestionOutcome>.Ok(outcome, StatusCodes.Status202Accepted);
        }

        var payload = new TelemetryPayload(
            record.AgentId,
            record.Timestamp,
            tier,
            record.RebalanceSignal,
            record.DiskAvailable);

        _producer.SendMessage(payload);
        _logger.LogInformation("[Telemetry] Enqueued payload for {AgentId}", record.AgentId);

        return ApiResult<IngestionOutcome>.Ok(outcome, StatusCodes.Status202Accepted);
    }
}
