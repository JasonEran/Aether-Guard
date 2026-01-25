using System.Text.Json;
using System.Text.Json.Serialization;
using AetherGuard.Core.Services;
using GrpcQueueCommandRequest = AetherGuard.Grpc.V1.QueueCommandRequest;
using Microsoft.AspNetCore.Mvc;

namespace AetherGuard.Core.Controllers;

[ApiController]
[Route("api/v1/commands")]
public class CommandsController : ControllerBase
{
    private readonly ControlPlaneService _controlPlaneService;
    private readonly ILogger<CommandsController> _logger;

    public CommandsController(ControlPlaneService controlPlaneService, ILogger<CommandsController> logger)
    {
        _controlPlaneService = controlPlaneService;
        _logger = logger;
    }

    [HttpPost("queue")]
    [HttpPost("/commands/queue")]
    public async Task<IActionResult> Queue([FromBody] QueueCommandRequest request, CancellationToken cancellationToken)
    {
        var parametersJson = request?.Params.ValueKind == JsonValueKind.Undefined
            ? string.Empty
            : request?.Params.GetRawText() ?? string.Empty;

        var grpcRequest = new GrpcQueueCommandRequest
        {
            WorkloadId = request?.WorkloadId ?? string.Empty,
            Action = request?.Action ?? string.Empty,
            Params = GrpcParameterConverter.ParseJsonStruct(parametersJson)
        };

        var result = await _controlPlaneService.QueueCommandAsync(grpcRequest, cancellationToken);
        if (!result.Success)
        {
            return StatusCode(result.StatusCode, result.ErrorPayload);
        }

        if (result.Payload is null)
        {
            return StatusCode(StatusCodes.Status500InternalServerError);
        }

        _logger.LogInformation("Queued command {CommandId} for workload {WorkloadId}", result.Payload.CommandId, request?.WorkloadId);

        return Accepted(new
        {
            status = result.Payload.Status,
            commandId = result.Payload.CommandId,
            nonce = result.Payload.Nonce,
            signature = result.Payload.Signature,
            expiresAt = result.Payload.ExpiresAt
        });
    }

    public sealed class QueueCommandRequest
    {
        public string WorkloadId { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;

        [JsonPropertyName("params")]
        public JsonElement Params { get; set; }
    }
}
