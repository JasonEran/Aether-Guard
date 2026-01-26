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
            Os = request?.Os ?? string.Empty,
            Capabilities = request?.Capabilities is null
                ? new AgentCapabilities()
                : new AgentCapabilities
                {
                    KernelVersion = request.Capabilities.KernelVersion ?? string.Empty,
                    CriuVersion = request.Capabilities.CriuVersion ?? string.Empty,
                    CriuAvailable = request.Capabilities.CriuAvailable,
                    EbpfAvailable = request.Capabilities.EbpfAvailable,
                    SupportsSnapshot = request.Capabilities.SupportsSnapshot,
                    SupportsNetTopology = request.Capabilities.SupportsNetTopology,
                    SupportsChaos = request.Capabilities.SupportsChaos
                }
        };

        var result = await _workflowService.RegisterAsync(grpcRequest, cancellationToken);
        if (!result.Success)
        {
            return StatusCode(result.StatusCode, result.ErrorPayload);
        }

        var config = result.Payload?.Config;
        var configPayload = config is null
            ? null
            : new
            {
                enableSnapshot = config.EnableSnapshot,
                enableEbpf = config.EnableEbpf,
                enableNetTopology = config.EnableNetTopology,
                enableChaos = config.EnableChaos,
                nodeMode = config.NodeMode
            };

        return Ok(new { token = result.Payload?.Token, agentId = result.Payload?.AgentId, config = configPayload });
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
        public AgentCapabilitiesRequest? Capabilities { get; set; }
    }

    public sealed class AgentCapabilitiesRequest
    {
        public string? KernelVersion { get; set; }
        public string? CriuVersion { get; set; }
        public bool CriuAvailable { get; set; }
        public bool EbpfAvailable { get; set; }
        public bool SupportsSnapshot { get; set; }
        public bool SupportsNetTopology { get; set; }
        public bool SupportsChaos { get; set; }
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
