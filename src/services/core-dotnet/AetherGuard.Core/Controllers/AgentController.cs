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

    public AgentController(ApplicationDbContext context)
    {
        _context = context;
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
            .Where(command => command.AgentId == agent.Id && command.Status == "PENDING")
            .OrderBy(command => command.CreatedAt)
            .ToListAsync(cancellationToken);

        if (pendingCommands.Count > 0)
        {
            foreach (var command in pendingCommands)
            {
                command.Status = "SENT";
            }
        }

        await _context.SaveChangesAsync(cancellationToken);

        var commandPayload = pendingCommands
            .Select(command => new { id = command.Id, type = command.CommandType })
            .ToArray();

        return Ok(new { status = "active", commands = commandPayload });
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
}
