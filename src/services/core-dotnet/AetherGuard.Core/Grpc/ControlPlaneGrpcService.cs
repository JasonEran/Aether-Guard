using AetherGuard.Core.Services;
using AetherGuard.Grpc.V1;
using Grpc.Core;

namespace AetherGuard.Core.Grpc;

public class ControlPlaneGrpcService : ControlPlane.ControlPlaneBase
{
    private readonly ControlPlaneService _controlPlaneService;

    public ControlPlaneGrpcService(ControlPlaneService controlPlaneService)
    {
        _controlPlaneService = controlPlaneService;
    }

    public override async Task<QueueCommandResponse> QueueCommand(
        QueueCommandRequest request,
        ServerCallContext context)
    {
        var result = await _controlPlaneService.QueueCommandAsync(request, context.CancellationToken);
        return HandleResult(result);
    }

    public override Task<DashboardLatestResponse> GetDashboardLatest(
        GetDashboardLatestRequest request,
        ServerCallContext context)
    {
        var result = _controlPlaneService.GetLatest();
        return Task.FromResult(HandleResult(result));
    }

    public override async Task<DashboardHistoryResponse> GetDashboardHistory(
        GetDashboardHistoryRequest request,
        ServerCallContext context)
    {
        var result = await _controlPlaneService.GetHistoryAsync(request, context.CancellationToken);
        return HandleResult(result);
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
