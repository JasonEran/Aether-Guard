using AetherGuard.Core.Models;
using AetherGuard.Core.Services;
using Microsoft.AspNetCore.Mvc;

namespace AetherGuard.Core.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class IngestionController : ControllerBase
{
    private readonly ILogger<IngestionController> _logger;
    private readonly TelemetryStore _telemetryStore;
    private readonly AnalysisService _analysisService;

    public IngestionController(
        ILogger<IngestionController> logger,
        TelemetryStore telemetryStore,
        AnalysisService analysisService)
    {
        _logger = logger;
        _telemetryStore = telemetryStore;
        _analysisService = analysisService;
    }

    // POST: api/v1/ingestion
    [HttpPost]
    public async Task<IActionResult> ReceiveTelemetry([FromBody] TelemetryPayload payload)
    {
        if (payload.CpuUsage < 0 || payload.CpuUsage > 100)
        {
            _logger.LogWarning("Received invalid CPU data from {AgentId}", payload.AgentId);
            return BadRequest("Invalid CPU Usage value.");
        }

        var analysis = await _analysisService.AnalyzeAsync(payload);
        _telemetryStore.Update(payload, analysis);

        var status = analysis?.Status ?? "Unavailable";
        var confidence = analysis?.Confidence ?? 0.0;

        _logger.LogInformation("[Telemetry] Agent: {Id} | CPU: {Cpu}% | Mem: {Mem}MB | AI: {Status} ({Confidence:P0})",
            payload.AgentId, payload.CpuUsage, payload.MemoryUsage, status, confidence);

        return Ok(new { status = "accepted", timestamp = DateTime.UtcNow });
    }
}
