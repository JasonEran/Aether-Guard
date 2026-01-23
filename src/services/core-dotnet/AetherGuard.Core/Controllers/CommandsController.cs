using System.Text.Json;
using System.Text.Json.Serialization;
using AetherGuard.Core.Services;
using Microsoft.AspNetCore.Mvc;

namespace AetherGuard.Core.Controllers;

[ApiController]
[Route("api/v1/commands")]
public class CommandsController : ControllerBase
{
    private readonly CommandService _commandService;
    private readonly ILogger<CommandsController> _logger;

    public CommandsController(CommandService commandService, ILogger<CommandsController> logger)
    {
        _commandService = commandService;
        _logger = logger;
    }

    [HttpPost("queue")]
    [HttpPost("/commands/queue")]
    public async Task<IActionResult> Queue([FromBody] QueueCommandRequest request, CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.WorkloadId))
        {
            return BadRequest(new { error = "WorkloadId is required." });
        }

        if (string.IsNullOrWhiteSpace(request.Action))
        {
            return BadRequest(new { error = "Action is required." });
        }

        var parameters = request.Params.ValueKind == JsonValueKind.Undefined
            ? new { }
            : request.Params;

        var command = await _commandService.QueueCommand(
            request.WorkloadId.Trim(),
            request.Action.Trim(),
            parameters,
            cancellationToken);

        _logger.LogInformation("Queued command {CommandId} for workload {WorkloadId}", command.CommandId, request.WorkloadId);

        return Accepted(new
        {
            status = "queued",
            commandId = command.CommandId,
            nonce = command.Nonce,
            signature = command.Signature,
            expiresAt = command.ExpiresAt
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
