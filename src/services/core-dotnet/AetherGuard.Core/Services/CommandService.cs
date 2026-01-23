using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AetherGuard.Core.Data;
using AetherGuard.Core.Models;

namespace AetherGuard.Core.Services;

public class CommandService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<CommandService> _logger;
    private readonly byte[] _signingKey;

    public CommandService(ApplicationDbContext context, IConfiguration configuration, ILogger<CommandService> logger)
    {
        _context = context;
        _logger = logger;

        var key = configuration["Security:CommandSigningKey"]
            ?? configuration["Security:CommandApiKey"]
            ?? "dev-secret";

        if (key == "dev-secret")
        {
            _logger.LogWarning("Command signing key not configured. Using dev-secret.");
        }

        _signingKey = Encoding.UTF8.GetBytes(key);
    }

    public async Task<AgentCommand> QueueCommand(string workloadId, string action, object parameters, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(workloadId))
        {
            throw new ArgumentException("workloadId is required.", nameof(workloadId));
        }

        if (string.IsNullOrWhiteSpace(action))
        {
            throw new ArgumentException("action is required.", nameof(action));
        }

        var normalizedAction = action.Trim().ToUpperInvariant();
        var commandId = Guid.NewGuid();
        var nonce = Guid.NewGuid().ToString("N");
        var expiresAt = DateTime.UtcNow.AddMinutes(5);
        var parametersJson = SerializeParameters(parameters);

        var signaturePayload = $"{commandId}{normalizedAction}{nonce}";
        var signature = ComputeSignature(signaturePayload);

        var agentId = Guid.TryParse(workloadId, out var parsedAgentId) ? parsedAgentId : Guid.Empty;

        var command = new AgentCommand
        {
            CommandId = commandId,
            AgentId = agentId,
            WorkloadId = workloadId,
            Action = normalizedAction,
            Parameters = parametersJson,
            Status = "PENDING",
            Nonce = nonce,
            Signature = signature,
            ExpiresAt = expiresAt,
            CreatedAt = DateTime.UtcNow
        };

        _context.AgentCommands.Add(command);
        _context.CommandAudits.Add(new CommandAudit
        {
            CommandId = commandId,
            Actor = "AI",
            Action = "Command Queued",
            Result = "PENDING",
            Error = string.Empty,
            CreatedAt = DateTime.UtcNow
        });

        await _context.SaveChangesAsync(cancellationToken);
        return command;
    }

    private string ComputeSignature(string payload)
    {
        using var hmac = new HMACSHA256(_signingKey);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToBase64String(hash);
    }

    private static string SerializeParameters(object parameters)
    {
        if (parameters is null)
        {
            return "{}";
        }

        if (parameters is string text)
        {
            return text;
        }

        return JsonSerializer.Serialize(parameters);
    }
}
