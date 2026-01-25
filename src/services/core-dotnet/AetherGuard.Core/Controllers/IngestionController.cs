using AetherGuard.Core.Models;
using AetherGuard.Core.Services;
using Microsoft.AspNetCore.Mvc;

namespace AetherGuard.Core.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class IngestionController : ControllerBase
{
    private readonly ILogger<IngestionController> _logger;
    private readonly TelemetryIngestionService _ingestionService;

    public IngestionController(
        ILogger<IngestionController> logger,
        TelemetryIngestionService ingestionService)
    {
        _logger = logger;
        _ingestionService = ingestionService;
    }

    // POST: api/v1/ingestion
    [HttpPost]
    public async Task<IActionResult> ReceiveTelemetry([FromBody] TelemetryPayload payload)
    {
        var record = new AetherGuard.Grpc.V1.TelemetryRecord
        {
            AgentId = payload?.AgentId ?? string.Empty,
            Timestamp = payload?.Timestamp ?? 0,
            WorkloadTier = payload?.WorkloadTier ?? string.Empty,
            RebalanceSignal = payload?.RebalanceSignal ?? false,
            DiskAvailable = payload?.DiskAvailable ?? 0
        };

        var result = await _ingestionService.IngestAsync(record, HttpContext.RequestAborted);
        if (!result.Success)
        {
            return StatusCode(result.StatusCode, result.ErrorPayload);
        }

        if (result.Payload is null)
        {
            return StatusCode(StatusCodes.Status500InternalServerError);
        }

        if (result.Payload.IsDuplicate)
        {
            _logger.LogInformation("[Telemetry] Duplicate payload ignored for {AgentId}", payload?.AgentId);
            return Accepted("Duplicate");
        }

        return Accepted(new { status = "queued", timestamp = result.Payload.EnqueuedAt.UtcDateTime });
    }
}
