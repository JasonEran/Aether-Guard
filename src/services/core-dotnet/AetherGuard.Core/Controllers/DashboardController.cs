using AetherGuard.Core.Data;
using AetherGuard.Core.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Mvc;

namespace AetherGuard.Core.Controllers;

[ApiController]
[Route("api/v1/dashboard")]
public class DashboardController : ControllerBase
{
    private readonly TelemetryStore _store;
    private readonly ApplicationDbContext _context;

    public DashboardController(TelemetryStore store, ApplicationDbContext context)
    {
        _store = store;
        _context = context;
    }

    [HttpGet("latest")]
    public IActionResult GetLatest()
    {
        var latest = _store.GetLatest();
        if (latest is null)
        {
            return NotFound();
        }

        var analysis = latest.Analysis is null
            ? null
            : new DashboardAnalysisDto(
                latest.Analysis.Status,
                latest.Analysis.Confidence,
                ClampPrediction(latest.Analysis.Prediction),
                latest.Analysis.RootCause);

        var response = new DashboardLatestDto(
            new DashboardTelemetryDto(
                latest.Telemetry.AgentId,
                latest.Telemetry.CpuUsage,
                latest.Telemetry.MemoryUsage,
                latest.Telemetry.Timestamp),
            analysis);

        return Ok(response);
    }

    [HttpGet("history")]
    public async Task<IActionResult> GetHistory()
    {
        var records = await _context.TelemetryRecords
            .OrderByDescending(x => x.Timestamp)
            .Take(20)
            .Select(record => new TelemetryHistoryDto(
                record.Id,
                record.AgentId,
                record.CpuUsage,
                record.MemoryUsage,
                record.AiStatus,
                record.AiConfidence,
                ClampPrediction(record.PredictedCpu),
                record.RootCause,
                record.Timestamp))
            .ToListAsync();

        records.Reverse();

        return Ok(records);
    }

    private sealed record DashboardLatestDto(
        DashboardTelemetryDto Telemetry,
        DashboardAnalysisDto? Analysis);

    private sealed record DashboardTelemetryDto(
        string AgentId,
        double CpuUsage,
        double MemoryUsage,
        long Timestamp);

    private sealed record DashboardAnalysisDto(
        string Status,
        double Confidence,
        double PredictedCpu,
        string RootCause);

    private sealed record TelemetryHistoryDto(
        long Id,
        string AgentId,
        double CpuUsage,
        double MemoryUsage,
        string AiStatus,
        double AiConfidence,
        double? PredictedCpu,
        string? RootCause,
        DateTime Timestamp);

    private static double ClampPrediction(double prediction)
        => Math.Clamp(prediction, 0, 100);

    private static double? ClampPrediction(double? prediction)
        => prediction.HasValue ? Math.Clamp(prediction.Value, 0, 100) : null;
}
