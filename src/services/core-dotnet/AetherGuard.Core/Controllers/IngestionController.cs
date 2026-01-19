using AetherGuard.Core.Models;
using Microsoft.AspNetCore.Mvc;

namespace AetherGuard.Core.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class IngestionController : ControllerBase
{
    private readonly ILogger<IngestionController> _logger;

    public IngestionController(ILogger<IngestionController> logger)
    {
        _logger = logger;
    }

    // POST: api/v1/ingestion
    [HttpPost]
    public IActionResult ReceiveTelemetry([FromBody] TelemetryPayload payload)
    {
        if (payload.CpuUsage < 0 || payload.CpuUsage > 100)
        {
            _logger.LogWarning("Received invalid CPU data from {AgentId}", payload.AgentId);
            return BadRequest("Invalid CPU Usage value.");
        }

        _logger.LogInformation("📡 [Telementry] Agent: {Id} | CPU: {Cpu}% | Mem: {Mem}MB", 
            payload.AgentId, payload.CpuUsage, payload.MemoryUsage);

        return Ok(new { status = "accepted", timestamp = DateTime.UtcNow });
    }
}