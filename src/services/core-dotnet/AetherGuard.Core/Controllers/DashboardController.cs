using System.Globalization;
using AetherGuard.Core.Services;
using AetherGuard.Grpc.V1;
using Microsoft.AspNetCore.Mvc;

namespace AetherGuard.Core.Controllers;

[ApiController]
[Route("api/v1/dashboard")]
public class DashboardController : ControllerBase
{
    private readonly ControlPlaneService _controlPlaneService;

    public DashboardController(ControlPlaneService controlPlaneService)
    {
        _controlPlaneService = controlPlaneService;
    }

    [HttpGet("latest")]
    public IActionResult GetLatest()
    {
        var result = _controlPlaneService.GetLatest();
        if (!result.Success)
        {
            return StatusCode(result.StatusCode);
        }

        if (result.Payload is null)
        {
            return StatusCode(StatusCodes.Status500InternalServerError);
        }

        var response = new DashboardLatestDto(
            new DashboardTelemetryDto(
                result.Payload.Telemetry.AgentId,
                result.Payload.Telemetry.WorkloadTier,
                result.Payload.Telemetry.RebalanceSignal,
                result.Payload.Telemetry.DiskAvailable,
                result.Payload.Telemetry.Timestamp),
            result.Payload.Analysis is null
                ? null
                : new DashboardAnalysisDto(
                    result.Payload.Analysis.Status,
                    result.Payload.Analysis.Confidence,
                    ClampPrediction(result.Payload.Analysis.PredictedCpu),
                    result.Payload.Analysis.RootCause));

        return Ok(response);
    }

    [HttpGet("history")]
    public async Task<IActionResult> GetHistory()
    {
        var result = await _controlPlaneService.GetHistoryAsync(
            new GetDashboardHistoryRequest { Limit = 20 },
            HttpContext.RequestAborted);
        if (!result.Success)
        {
            return StatusCode(result.StatusCode);
        }

        if (result.Payload is null)
        {
            return StatusCode(StatusCodes.Status500InternalServerError);
        }

        var records = result.Payload.Records
            .Select(record => new TelemetryHistoryDto(
                record.Id,
                record.AgentId,
                record.WorkloadTier,
                record.RebalanceSignal,
                record.DiskAvailable,
                record.AiStatus,
                record.AiConfidence,
                ClampPrediction(record.PredictedCpu),
                string.IsNullOrWhiteSpace(record.RootCause) ? null : record.RootCause,
                DateTime.Parse(record.Timestamp, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind)))
            .ToList();

        return Ok(records);
    }

    private sealed record DashboardLatestDto(
        DashboardTelemetryDto Telemetry,
        DashboardAnalysisDto? Analysis);

    private sealed record DashboardTelemetryDto(
        string AgentId,
        string WorkloadTier,
        bool RebalanceSignal,
        long DiskAvailable,
        long Timestamp);

    private sealed record DashboardAnalysisDto(
        string Status,
        double Confidence,
        double PredictedCpu,
        string RootCause);

    private sealed record TelemetryHistoryDto(
        long Id,
        string AgentId,
        string WorkloadTier,
        bool RebalanceSignal,
        long DiskAvailable,
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
