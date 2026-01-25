using AetherGuard.Core.Services;
using AetherGuard.Grpc.V1;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AetherGuard.Core.Controllers;

[ApiController]
[Route("api/v1/agent")]
public class AgentController : ControllerBase
{
    private readonly AgentWorkflowService _workflowService;

    public AgentController(AgentWorkflowService workflowService)
    {
        _workflowService = workflowService;
    }

    [HttpPost("register")]
    public async Task<IActionResult> RegisterAgent([FromBody] RegisterAgentRequest request, CancellationToken cancellationToken)
    {
        var grpcRequest = new RegisterRequest
        {
            Hostname = request?.Hostname ?? string.Empty,
            Os = request?.Os ?? string.Empty
        };

        var result = await _workflowService.RegisterAsync(grpcRequest, cancellationToken);
        if (!result.Success)
        {
            return StatusCode(result.StatusCode, result.ErrorPayload);
        }

        return Ok(new { token = result.Payload?.Token, agentId = result.Payload?.AgentId });
    }

    [HttpPost("heartbeat")]
    public async Task<IActionResult> Heartbeat([FromBody] HeartbeatRequest request, CancellationToken cancellationToken)
    {
        var grpcRequest = new AetherGuard.Grpc.V1.HeartbeatRequest
        {
            Token = request?.Token ?? string.Empty
        };

        var result = await _workflowService.HeartbeatAsync(grpcRequest, cancellationToken);
        if (!result.Success)
        {
            return StatusCode(result.StatusCode, result.ErrorPayload);
        }
        if (result.Payload is null)
        {
            return StatusCode(StatusCodes.Status500InternalServerError);
        }

        var commandPayload = result.Payload.Commands
            .Select(command => new
            {
                id = command.Id,
                commandId = command.CommandId,
                action = command.Action,
                workloadId = command.WorkloadId,
                parameters = GrpcParameterConverter.FormatStruct(command.Parameters),
                nonce = command.Nonce,
                signature = command.Signature,
                expiresAt = command.ExpiresAt
            })
            .ToArray();

        return Ok(new { status = result.Payload.Status, commands = commandPayload });
    }

    [HttpGet("poll")]
    [HttpGet("/poll")]
    public async Task<IActionResult> Poll([FromQuery] Guid agentId, CancellationToken cancellationToken)
    {
        var grpcRequest = new PollCommandsRequest { AgentId = agentId.ToString() };
        var result = await _workflowService.PollCommandsAsync(grpcRequest, cancellationToken);
        if (!result.Success)
        {
            return StatusCode(result.StatusCode, result.ErrorPayload);
        }
        if (result.Payload is null)
        {
            return StatusCode(StatusCodes.Status500InternalServerError);
        }

        var payload = result.Payload.Commands
            .Select(command => new
            {
                commandId = command.CommandId,
                workloadId = command.WorkloadId,
                action = command.Action,
                parameters = GrpcParameterConverter.FormatStruct(command.Parameters),
                nonce = command.Nonce,
                signature = command.Signature,
                expiresAt = command.ExpiresAt
            })
            .ToArray();

        return Ok(new { commands = payload });
    }

    [HttpPost("feedback")]
    [HttpPost("/feedback")]
    public async Task<IActionResult> Feedback([FromBody] CommandFeedbackRequest request, CancellationToken cancellationToken)
    {
        var grpcRequest = new FeedbackRequest
        {
            AgentId = request?.AgentId.ToString() ?? string.Empty,
            CommandId = request?.CommandId.ToString() ?? string.Empty,
            Status = request?.Status ?? string.Empty,
            Result = request?.Result ?? string.Empty,
            Error = request?.Error ?? string.Empty
        };

        var result = await _workflowService.FeedbackAsync(grpcRequest, cancellationToken);
        if (!result.Success)
        {
            return StatusCode(result.StatusCode, result.ErrorPayload);
        }

        return Ok(new { status = result.Payload?.Status });
    }

    public sealed class RegisterAgentRequest
    {
        public string Hostname { get; set; } = string.Empty;
        public string Os { get; set; } = string.Empty;
    }

    public sealed class HeartbeatRequest
    {
        public string Token { get; set; } = string.Empty;
    }

    public sealed class CommandFeedbackRequest
    {
        public Guid AgentId { get; set; }
        public Guid CommandId { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? Result { get; set; }
        public string? Error { get; set; }
    }
}
