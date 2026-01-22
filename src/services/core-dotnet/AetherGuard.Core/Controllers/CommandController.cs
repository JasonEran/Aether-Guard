using AetherGuard.Core.Data;
using AetherGuard.Core.Models;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AetherGuard.Core.Controllers;

[ApiController]
[Route("api/v1/agents")]
public class CommandController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<CommandController> _logger;
    private readonly string? _commandApiKey;

    public CommandController(ApplicationDbContext context, IConfiguration configuration, ILogger<CommandController> logger)
    {
        _context = context;
        _logger = logger;
        _commandApiKey = configuration["Security:CommandApiKey"];
    }

    [HttpPost("{agentId:guid}/commands")]
    public async Task<IActionResult> CreateCommand(Guid agentId, [FromBody] CommandRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_commandApiKey))
        {
            _logger.LogError("Security:CommandApiKey is not configured.");
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Command API key not configured." });
        }

        if (!Request.Headers.TryGetValue("X-API-Key", out var providedKey))
        {
            return Unauthorized(new { error = "Missing API key." });
        }

        if (!FixedTimeEquals(providedKey.ToString(), _commandApiKey))
        {
            return Unauthorized(new { error = "Invalid API key." });
        }

        if (request is null || string.IsNullOrWhiteSpace(request.Type))
        {
            return BadRequest(new { error = "Command type is required." });
        }

        var commandType = request.Type.Trim().ToUpperInvariant();
        if (commandType != "RESTART")
        {
            return BadRequest(new { error = "Unsupported command type." });
        }

        var agentExists = await _context.Agents.AnyAsync(agent => agent.Id == agentId, cancellationToken);
        if (!agentExists)
        {
            return NotFound(new { error = "Agent not found." });
        }

        var command = new AgentCommand
        {
            AgentId = agentId,
            CommandType = commandType,
            Status = "PENDING",
            CreatedAt = DateTime.UtcNow
        };

        _context.AgentCommands.Add(command);
        await _context.SaveChangesAsync(cancellationToken);

        return Accepted(new { status = "queued", commandId = command.Id });
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);

        return leftBytes.Length == rightBytes.Length
            && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }

    public sealed class CommandRequest
    {
        public string Type { get; set; } = string.Empty;
    }
}
