using AetherGuard.Core.Data;
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
    private readonly ApplicationDbContext _context;

    public IngestionController(
        ILogger<IngestionController> logger,
        TelemetryStore telemetryStore,
        AnalysisService analysisService,
        ApplicationDbContext context)
    {
        _logger = logger;
        _telemetryStore = telemetryStore;
        _analysisService = analysisService;
        _context = context;
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
        var status = analysis?.Status ?? "Unavailable";
        var confidence = analysis?.Confidence ?? 0.0;

        var record = new TelemetryRecord
        {
            AgentId = payload.AgentId,
            CpuUsage = payload.CpuUsage,
            MemoryUsage = payload.MemoryUsage,
            AiStatus = status,
            AiConfidence = confidence,
            Timestamp = DateTimeOffset.FromUnixTimeSeconds(payload.Timestamp).UtcDateTime
        };

        _context.TelemetryRecords.Add(record);
        await _context.SaveChangesAsync();

        _telemetryStore.Update(payload, analysis);

        _logger.LogInformation("[Telemetry] Agent: {Id} | CPU: {Cpu}% | Mem: {Mem}MB | AI: {Status} ({Confidence:P0})",
            payload.AgentId, payload.CpuUsage, payload.MemoryUsage, status, confidence);

        return Ok(new { status = "accepted", timestamp = DateTime.UtcNow });
    }
}
