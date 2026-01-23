using AetherGuard.Core.Data;
using AetherGuard.Core.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AetherGuard.Core.Controllers;

[ApiController]
[Route("api/v1/agent")]
public class AgentController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<AgentController> _logger;

    public AgentController(ApplicationDbContext context, ILogger<AgentController> logger)
    {
        _context = context;
        _logger = logger;
    }

    [HttpPost("register")]
    public async Task<IActionResult> RegisterAgent([FromBody] RegisterAgentRequest request, CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Hostname))
        {
            return BadRequest(new { error = "Hostname is required." });
        }

        var hostname = request.Hostname.Trim();
        var existingAgent = await _context.Agents
            .FirstOrDefaultAsync(agent => agent.Hostname == hostname, cancellationToken);

        if (existingAgent is not null)
        {
            return Ok(new { token = existingAgent.AgentToken, agentId = existingAgent.Id });
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

        return Ok(new { token = agent.AgentToken, agentId = agent.Id });
    }

    [HttpPost("heartbeat")]
    public async Task<IActionResult> Heartbeat([FromBody] HeartbeatRequest request, CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Token))
        {
            return BadRequest(new { error = "Token is required." });
        }

        var token = request.Token.Trim();
        var agent = await _context.Agents
            .FirstOrDefaultAsync(existing => existing.AgentToken == token, cancellationToken);

        if (agent is null)
        {
            return Unauthorized(new { error = "Invalid token." });
        }

        agent.LastHeartbeat = DateTimeOffset.UtcNow;
        agent.Status = "ONLINE";

        var pendingCommands = await _context.AgentCommands
            .AsNoTracking()
            .Where(command => command.AgentId == agent.Id && command.Status == "PENDING")
            .OrderBy(command => command.CreatedAt)
            .ToListAsync(cancellationToken);

        await _context.SaveChangesAsync(cancellationToken);

        var commandPayload = pendingCommands
            .Select(command => new
            {
                id = command.Id,
                commandId = command.CommandId,
                action = command.Action,
                workloadId = command.WorkloadId,
                parameters = command.Parameters,
                nonce = command.Nonce,
                signature = command.Signature,
                expiresAt = command.ExpiresAt
            })
            .ToArray();

        return Ok(new { status = "active", commands = commandPayload });
    }

    [HttpGet("poll")]
    [HttpGet("/poll")]
    public async Task<IActionResult> Poll([FromQuery] Guid agentId, CancellationToken cancellationToken)
    {
        if (agentId == Guid.Empty)
        {
            return BadRequest(new { error = "AgentId is required." });
        }

        var commands = await _context.AgentCommands
            .AsNoTracking()
            .Where(command => command.AgentId == agentId
                && command.Status == "PENDING")
            .OrderBy(command => command.CreatedAt)
            .ToListAsync(cancellationToken);

        var payload = commands
            .Select(command => new
            {
                commandId = command.CommandId,
                workloadId = command.WorkloadId,
                action = command.Action,
                parameters = command.Parameters,
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
        if (request is null || request.CommandId == Guid.Empty || request.AgentId == Guid.Empty)
        {
            return BadRequest(new { error = "AgentId and CommandId are required." });
        }

        var status = request.Status?.Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(status))
        {
            return BadRequest(new { error = "Status is required." });
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
            return BadRequest(new { error = "Unsupported status." });
        }

        var command = await _context.AgentCommands
            .FirstOrDefaultAsync(
                existing => existing.CommandId == request.CommandId && existing.AgentId == request.AgentId,
                cancellationToken);

        if (command is null)
        {
            return NotFound(new { error = "Command not found." });
        }

        command.Status = normalizedStatus;
        command.UpdatedAt = DateTime.UtcNow;

        _context.CommandAudits.Add(new CommandAudit
        {
            CommandId = command.CommandId,
            Actor = request.AgentId.ToString(),
            Action = "Execution Result Received",
            Result = request.Result ?? status,
            Error = request.Error ?? string.Empty,
            CreatedAt = DateTime.UtcNow
        });

        await _context.SaveChangesAsync(cancellationToken);

        if (normalizedStatus == "FAILED")
        {
            _logger.LogWarning("TRIGGER FALLBACK THAW for command {CommandId}", command.CommandId);
        }

        return Ok(new { status = command.Status });
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
