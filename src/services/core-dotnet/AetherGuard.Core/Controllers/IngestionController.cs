using AetherGuard.Core.Models;
using AetherGuard.Core.Services;
using AetherGuard.Core.Services.Messaging;
using Microsoft.AspNetCore.Mvc;
using StackExchange.Redis;

namespace AetherGuard.Core.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class IngestionController : ControllerBase
{
    private readonly ILogger<IngestionController> _logger;
    private readonly IConnectionMultiplexer _redis;
    private readonly IMessageProducer _producer;

    public IngestionController(
        ILogger<IngestionController> logger,
        IConnectionMultiplexer redis,
        IMessageProducer producer)
    {
        _logger = logger;
        _redis = redis;
        _producer = producer;
    }

    // POST: api/v1/ingestion
    [HttpPost]
    public async Task<IActionResult> ReceiveTelemetry([FromBody] TelemetryPayload payload)
    {
        if (payload is null)
        {
            return BadRequest("Telemetry payload is required.");
        }

        if (string.IsNullOrWhiteSpace(payload.AgentId))
        {
            return BadRequest("AgentId is required.");
        }

        if (payload.Timestamp <= 0)
        {
            return BadRequest("Timestamp is required.");
        }

        if (string.IsNullOrWhiteSpace(payload.WorkloadTier))
        {
            return BadRequest("WorkloadTier is required.");
        }

        var tier = payload.WorkloadTier.Trim().ToUpperInvariant();
        if (tier is not ("T1" or "T2" or "T3"))
        {
            _logger.LogWarning("Received invalid tier data from {AgentId}", payload.AgentId);
            return BadRequest("Invalid WorkloadTier value.");
        }

        if (payload.DiskAvailable < 0)
        {
            _logger.LogWarning("Received invalid disk data from {AgentId}", payload.AgentId);
            return BadRequest("Invalid DiskAvailable value.");
        }

        var dedupKey = $"dedup:{payload.AgentId}:{payload.Timestamp}";
        var database = _redis.GetDatabase();
        var setResult = await database.StringSetAsync(dedupKey, "1", TimeSpan.FromSeconds(10), When.NotExists);

        if (!setResult)
        {
            _logger.LogInformation("[Telemetry] Duplicate payload ignored for {AgentId}", payload.AgentId);
            return Accepted("Duplicate");
        }

        _producer.SendMessage(payload);
        _logger.LogInformation("[Telemetry] Enqueued payload for {AgentId}", payload.AgentId);

        return Accepted(new { status = "queued", timestamp = DateTime.UtcNow });
    }
}
