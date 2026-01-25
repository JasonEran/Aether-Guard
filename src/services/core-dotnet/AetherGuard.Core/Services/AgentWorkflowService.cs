using AetherGuard.Core.Data;
using AetherGuard.Core.Models;
using CoreAgentCommand = AetherGuard.Core.Models.AgentCommand;
using AetherGuard.Grpc.V1;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace AetherGuard.Core.Services;

public class AgentWorkflowService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<AgentWorkflowService> _logger;

    public AgentWorkflowService(ApplicationDbContext context, ILogger<AgentWorkflowService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task<ApiResult<RegisterResponse>> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Hostname))
        {
            return ApiResult<RegisterResponse>.Fail(
                StatusCodes.Status400BadRequest,
                "Hostname is required.");
        }

        var hostname = request.Hostname.Trim();
        var existingAgent = await _context.Agents
            .FirstOrDefaultAsync(agent => agent.Hostname == hostname, cancellationToken);

        if (existingAgent is not null)
        {
            return ApiResult<RegisterResponse>.Ok(new RegisterResponse
            {
                Token = existingAgent.AgentToken,
                AgentId = existingAgent.Id.ToString()
            });
        }

        var agent = new Agent
        {
            Id = Guid.NewGuid(),
            AgentToken = Guid.NewGuid().ToString("N"),
            Hostname = hostname,
            Status = "OFFLINE",
            LastHeartbeat = DateTimeOffset.UtcNow
        };

        _context.Agents.Add(agent);
        await _context.SaveChangesAsync(cancellationToken);

        return ApiResult<RegisterResponse>.Ok(new RegisterResponse
        {
            Token = agent.AgentToken,
            AgentId = agent.Id.ToString()
        });
    }

    public async Task<ApiResult<HeartbeatResponse>> HeartbeatAsync(HeartbeatRequest request, CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Token))
        {
            return ApiResult<HeartbeatResponse>.Fail(
                StatusCodes.Status400BadRequest,
                "Token is required.");
        }

        var token = request.Token.Trim();
        var agent = await _context.Agents
            .FirstOrDefaultAsync(existing => existing.AgentToken == token, cancellationToken);

        if (agent is null)
        {
            return ApiResult<HeartbeatResponse>.Fail(
                StatusCodes.Status401Unauthorized,
                "Invalid token.");
        }

        agent.LastHeartbeat = DateTimeOffset.UtcNow;
        agent.Status = "ONLINE";

        var pendingCommands = await _context.AgentCommands
            .AsNoTracking()
            .Where(command => command.AgentId == agent.Id && command.Status == "PENDING")
            .OrderBy(command => command.CreatedAt)
            .ToListAsync(cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);

        var response = new HeartbeatResponse { Status = "active" };
        response.Commands.AddRange(pendingCommands.Select(MapCommand));

        return ApiResult<HeartbeatResponse>.Ok(response);
    }

    public async Task<ApiResult<PollCommandsResponse>> PollCommandsAsync(PollCommandsRequest request, CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.AgentId))
        {
            return ApiResult<PollCommandsResponse>.Fail(
                StatusCodes.Status400BadRequest,
                "AgentId is required.");
        }

        if (!Guid.TryParse(request.AgentId, out var agentId) || agentId == Guid.Empty)
        {
            return ApiResult<PollCommandsResponse>.Fail(
                StatusCodes.Status400BadRequest,
                "AgentId is required.");
        }

        var commands = await _context.AgentCommands
            .AsNoTracking()
            .Where(command => command.AgentId == agentId && command.Status == "PENDING")
            .OrderBy(command => command.CreatedAt)
            .ToListAsync(cancellationToken);

        var response = new PollCommandsResponse();
        response.Commands.AddRange(commands.Select(MapCommand));

        return ApiResult<PollCommandsResponse>.Ok(response);
    }

    public async Task<ApiResult<FeedbackResponse>> FeedbackAsync(FeedbackRequest request, CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.AgentId) || string.IsNullOrWhiteSpace(request.CommandId))
        {
            return ApiResult<FeedbackResponse>.Fail(
                StatusCodes.Status400BadRequest,
                "AgentId and CommandId are required.");
        }

        if (!Guid.TryParse(request.AgentId, out var agentId) || agentId == Guid.Empty
            || !Guid.TryParse(request.CommandId, out var commandId) || commandId == Guid.Empty)
        {
            return ApiResult<FeedbackResponse>.Fail(
                StatusCodes.Status400BadRequest,
                "AgentId and CommandId are required.");
        }

        var status = request.Status.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(status))
        {
            return ApiResult<FeedbackResponse>.Fail(
                StatusCodes.Status400BadRequest,
                "Status is required.");
        }

        var normalizedStatus = status switch
        {
            "COMPLETED" => "COMPLETED",
            "FAILED" => "FAILED",
            "DUPLICATE" => "COMPLETED",
            _ => string.Empty
        };

        if (string.IsNullOrEmpty(normalizedStatus))
        {
            return ApiResult<FeedbackResponse>.Fail(
                StatusCodes.Status400BadRequest,
                "Unsupported status.");
        }

        var command = await _context.AgentCommands
            .FirstOrDefaultAsync(
                existing => existing.CommandId == commandId && existing.AgentId == agentId,
                cancellationToken);

        if (command is null)
        {
            return ApiResult<FeedbackResponse>.Fail(
                StatusCodes.Status404NotFound,
                "Command not found.");
        }

        command.Status = normalizedStatus;
        command.UpdatedAt = DateTime.UtcNow;

        _context.CommandAudits.Add(new CommandAudit
        {
            CommandId = command.CommandId,
            Actor = request.AgentId,
            Action = "Execution Result Received",
            Result = string.IsNullOrWhiteSpace(request.Result) ? status : request.Result,
            Error = request.Error,
            CreatedAt = DateTime.UtcNow
        });

        await _context.SaveChangesAsync(cancellationToken);

        if (normalizedStatus == "FAILED")
        {
            _logger.LogWarning("TRIGGER FALLBACK THAW for command {CommandId}", command.CommandId);
        }

        return ApiResult<FeedbackResponse>.Ok(new FeedbackResponse { Status = command.Status });
    }

    private static AetherGuard.Grpc.V1.AgentCommand MapCommand(CoreAgentCommand command)
    {
        var parameters = GrpcParameterConverter.ParseJsonStruct(command.Parameters);

        return new AetherGuard.Grpc.V1.AgentCommand
        {
            Id = command.Id,
            CommandId = command.CommandId.ToString(),
            WorkloadId = command.WorkloadId,
            Action = command.Action,
            Parameters = parameters,
            Nonce = command.Nonce,
            Signature = command.Signature,
            ExpiresAt = command.ExpiresAt.ToString("O")
        };
    }
}
