using Microsoft.AspNetCore.Http;

namespace AetherGuard.Core.Services;

public sealed record ApiResult<T>(int StatusCode, T? Payload, string? ErrorMessage, object? ErrorPayload)
{
    public bool Success => StatusCode < 400;

    public static ApiResult<T> Ok(T payload, int statusCode = StatusCodes.Status200OK)
        => new(statusCode, payload, null, null);

    public static ApiResult<T> Fail(int statusCode, string errorMessage, object? errorPayload = null)
        => new(statusCode, default, errorMessage, errorPayload ?? new { error = errorMessage });
}
