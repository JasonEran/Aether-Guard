using Grpc.Core;
using Microsoft.AspNetCore.Http;

namespace AetherGuard.Core.Grpc;

public static class GrpcErrorHelper
{
    public static RpcException ToRpcException(string message, int statusCode)
    {
        var status = statusCode switch
        {
            StatusCodes.Status400BadRequest => StatusCode.InvalidArgument,
            StatusCodes.Status401Unauthorized => StatusCode.Unauthenticated,
            StatusCodes.Status403Forbidden => StatusCode.PermissionDenied,
            StatusCodes.Status404NotFound => StatusCode.NotFound,
            StatusCodes.Status409Conflict => StatusCode.AlreadyExists,
            _ => StatusCode.Internal
        };

        return new RpcException(new Status(status, message));
    }
}
