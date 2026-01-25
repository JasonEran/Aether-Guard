using AetherGuard.Core.Services;
using AetherGuard.Grpc.V1;
using Grpc.Core;

namespace AetherGuard.Core.Grpc;

public class AgentGrpcService : AgentService.AgentServiceBase
{
    private readonly AgentWorkflowService _workflowService;
    private readonly TelemetryIngestionService _telemetryIngestionService;

    public AgentGrpcService(
        AgentWorkflowService workflowService,
        TelemetryIngestionService telemetryIngestionService)
    {
        _workflowService = workflowService;
        _telemetryIngestionService = telemetryIngestionService;
    }

    public override async Task<RegisterResponse> Register(RegisterRequest request, ServerCallContext context)
    {
        var result = await _workflowService.RegisterAsync(request, context.CancellationToken);
        return HandleResult(result);
    }

    public override async Task<HeartbeatResponse> Heartbeat(HeartbeatRequest request, ServerCallContext context)
    {
        var result = await _workflowService.HeartbeatAsync(request, context.CancellationToken);
        return HandleResult(result);
    }

    public override async Task<PollCommandsResponse> PollCommands(PollCommandsRequest request, ServerCallContext context)
    {
        var result = await _workflowService.PollCommandsAsync(request, context.CancellationToken);
        return HandleResult(result);
    }

    public override async Task<FeedbackResponse> SendFeedback(FeedbackRequest request, ServerCallContext context)
    {
        var result = await _workflowService.FeedbackAsync(request, context.CancellationToken);
        return HandleResult(result);
    }

    public override async Task<TelemetryAck> IngestTelemetry(
        AetherGuard.Grpc.V1.TelemetryRecord request,
        ServerCallContext context)
    {
        var result = await _telemetryIngestionService.IngestAsync(request, context.CancellationToken);
        if (!result.Success || result.Payload is null)
        {
            throw GrpcErrorHelper.ToRpcException(
                result.ErrorMessage ?? "Telemetry ingestion failed.",
                result.StatusCode);
        }

        return result.Payload.Ack;
    }

    private static T HandleResult<T>(ApiResult<T> result)
    {
        if (result.Success && result.Payload is not null)
        {
            return result.Payload;
        }

        throw GrpcErrorHelper.ToRpcException(
            result.ErrorMessage ?? "Request failed.",
            result.StatusCode);
    }
}
