using AetherGuard.Core.Data;
using AetherGuard.Grpc.V1;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace AetherGuard.Core.Services;

public class ControlPlaneService
{
    private readonly ApplicationDbContext _context;
    private readonly CommandService _commandService;
    private readonly TelemetryStore _telemetryStore;

    public ControlPlaneService(
        ApplicationDbContext context,
        CommandService commandService,
        TelemetryStore telemetryStore)
    {
        _context = context;
        _commandService = commandService;
        _telemetryStore = telemetryStore;
    }

    public async Task<ApiResult<QueueCommandResponse>> QueueCommandAsync(
        QueueCommandRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.WorkloadId))
        {
            return ApiResult<QueueCommandResponse>.Fail(
                StatusCodes.Status400BadRequest,
                "WorkloadId is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Action))
        {
            return ApiResult<QueueCommandResponse>.Fail(
                StatusCodes.Status400BadRequest,
                "Action is required.");
        }

        object parameters = request.Params is null || request.Params.Fields.Count == 0
            ? new object()
            : GrpcParameterConverter.FormatStruct(request.Params);

        var command = await _commandService.QueueCommand(
            request.WorkloadId.Trim(),
            request.Action.Trim(),
            parameters,
            cancellationToken);

        var response = new QueueCommandResponse
        {
            Status = "queued",
            CommandId = command.CommandId.ToString(),
            Nonce = command.Nonce,
            Signature = command.Signature,
            ExpiresAt = command.ExpiresAt.ToString("O")
        };

        return ApiResult<QueueCommandResponse>.Ok(response, StatusCodes.Status202Accepted);
    }

    public ApiResult<DashboardLatestResponse> GetLatest()
    {
        var latest = _telemetryStore.GetLatest();
        if (latest is null)
        {
            return ApiResult<DashboardLatestResponse>.Fail(
                StatusCodes.Status404NotFound,
                "Not found.");
        }

        var telemetry = new DashboardTelemetry
        {
            AgentId = latest.Telemetry.AgentId,
            WorkloadTier = latest.Telemetry.WorkloadTier,
            RebalanceSignal = latest.Telemetry.RebalanceSignal,
            DiskAvailable = latest.Telemetry.DiskAvailable,
            Timestamp = latest.Telemetry.Timestamp
        };

        DashboardAnalysis? analysis = null;
        if (latest.Analysis is not null)
        {
            analysis = new DashboardAnalysis
            {
                Status = latest.Analysis.Status,
                Confidence = latest.Analysis.Confidence,
                PredictedCpu = ClampPrediction(latest.Analysis.Prediction),
                RootCause = latest.Analysis.RootCause ?? string.Empty
            };
        }

        var response = new DashboardLatestResponse
        {
            Telemetry = telemetry
        };

        if (analysis is not null)
        {
            response.Analysis = analysis;
        }

        return ApiResult<DashboardLatestResponse>.Ok(response);
    }

    public async Task<ApiResult<DashboardHistoryResponse>> GetHistoryAsync(
        GetDashboardHistoryRequest request,
        CancellationToken cancellationToken)
    {
        var limit = request?.Limit > 0 ? request.Limit : 20;

        var records = await _context.TelemetryRecords
            .OrderByDescending(x => x.Timestamp)
            .Take(limit)
            .Select(record => new TelemetryHistoryEntry
            {
                Id = record.Id,
                AgentId = record.AgentId,
                WorkloadTier = record.WorkloadTier,
                RebalanceSignal = record.RebalanceSignal,
                DiskAvailable = record.DiskAvailable,
                AiStatus = record.AiStatus,
                AiConfidence = record.AiConfidence,
                PredictedCpu = ClampPrediction(record.PredictedCpu),
                RootCause = record.RootCause ?? string.Empty,
                Timestamp = record.Timestamp.ToString("O")
            })
            .ToListAsync(cancellationToken);

        records.Reverse();

        var response = new DashboardHistoryResponse();
        response.Records.AddRange(records);

        return ApiResult<DashboardHistoryResponse>.Ok(response);
    }

    private static double ClampPrediction(double prediction)
        => Math.Clamp(prediction, 0, 100);

    private static double ClampPrediction(double? prediction)
        => prediction.HasValue ? Math.Clamp(prediction.Value, 0, 100) : 0;
}
