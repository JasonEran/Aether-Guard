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

        if (payload.CpuUsage < 0 || payload.CpuUsage > 100)
        {
            _logger.LogWarning("Received invalid CPU data from {AgentId}", payload.AgentId);
            return BadRequest("Invalid CPU Usage value.");
        }

        if (payload.MemoryUsage < 0 || payload.MemoryUsage > 100)
        {
            _logger.LogWarning("Received invalid memory data from {AgentId}", payload.AgentId);
            return BadRequest("Invalid Memory Usage value.");
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
