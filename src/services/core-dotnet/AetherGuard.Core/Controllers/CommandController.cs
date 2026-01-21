using AetherGuard.Core.Data;
using AetherGuard.Core.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AetherGuard.Core.Controllers;

[ApiController]
[Route("api/v1/agents")]
public class CommandController : ControllerBase
{
    private readonly ApplicationDbContext _context;

    public CommandController(ApplicationDbContext context)
    {
        _context = context;
    }

    [HttpPost("{agentId:guid}/commands")]
    public async Task<IActionResult> CreateCommand(Guid agentId, [FromBody] CommandRequest request, CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Type))
        {
            return BadRequest(new { error = "Command type is required." });
        }

        var agentExists = await _context.Agents.AnyAsync(agent => agent.Id == agentId, cancellationToken);
        if (!agentExists)
        {
            return NotFound(new { error = "Agent not found." });
        }

        var command = new AgentCommand
        {
            AgentId = agentId,
            CommandType = request.Type.Trim().ToUpperInvariant(),
            Status = "PENDING",
            CreatedAt = DateTime.UtcNow
        };

        _context.AgentCommands.Add(command);
        await _context.SaveChangesAsync(cancellationToken);

        return Accepted(new { status = "queued", commandId = command.Id });
    }

    public sealed class CommandRequest
    {
        public string Type { get; set; } = string.Empty;
    }
}
