using System.Security.Cryptography;
using AetherGuard.Core.Models;
using AetherGuard.Core.Services;
using Microsoft.AspNetCore.Mvc;

namespace AetherGuard.Core.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class IngestionController : ControllerBase
{
    private const string ApiKeyHeader = "X-API-Key";
    private readonly ILogger<IngestionController> _logger;
    private readonly TelemetryIngestionService _ingestionService;
    private readonly string? _telemetryApiKey;

    public IngestionController(
        ILogger<IngestionController> logger,
        TelemetryIngestionService ingestionService,
        IConfiguration configuration)
    {
        _logger = logger;
        _ingestionService = ingestionService;
        _telemetryApiKey = configuration["Security:TelemetryApiKey"]
            ?? configuration["Security:CommandApiKey"];
    }

    // POST: api/v1/ingestion
    [HttpPost]
    public async Task<IActionResult> ReceiveTelemetry([FromBody] TelemetryPayload payload)
    {
        if (!TryAuthorize(out var failure))
        {
            return failure;
        }

        var record = new AetherGuard.Grpc.V1.TelemetryRecord
        {
            AgentId = payload?.AgentId ?? string.Empty,
            Timestamp = payload?.Timestamp ?? 0,
            WorkloadTier = payload?.WorkloadTier ?? string.Empty,
            RebalanceSignal = payload?.RebalanceSignal ?? false,
            DiskAvailable = payload?.DiskAvailable ?? 0
        };

        var result = await _ingestionService.IngestAsync(record, HttpContext.RequestAborted);
        if (!result.Success)
        {
            return StatusCode(result.StatusCode, result.ErrorPayload);
        }

        if (result.Payload is null)
        {
            return StatusCode(StatusCodes.Status500InternalServerError);
        }

        if (result.Payload.IsDuplicate)
        {
            _logger.LogInformation("[Telemetry] Duplicate payload ignored for {AgentId}", payload?.AgentId);
            return Accepted("Duplicate");
        }

        return Accepted(new { status = "queued", timestamp = result.Payload.EnqueuedAt.UtcDateTime });
    }

    private bool TryAuthorize(out IActionResult failure)
    {
        if (string.IsNullOrWhiteSpace(_telemetryApiKey))
        {
            _logger.LogError("Security:TelemetryApiKey is not configured.");
            failure = StatusCode(StatusCodes.Status500InternalServerError, new { error = "Telemetry API key not configured." });
            return false;
        }

        if (!Request.Headers.TryGetValue(ApiKeyHeader, out var providedKey))
        {
            failure = Unauthorized(new { error = "Missing API key." });
            return false;
        }

        if (!FixedTimeEquals(providedKey.ToString(), _telemetryApiKey))
        {
            failure = Unauthorized(new { error = "Invalid API key." });
            return false;
        }

        failure = null!;
        return true;
    }

    private static bool FixedTimeEquals(string provided, string expected)
    {
        var providedBytes = System.Text.Encoding.UTF8.GetBytes(provided);
        var expectedBytes = System.Text.Encoding.UTF8.GetBytes(expected);
        if (providedBytes.Length != expectedBytes.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(providedBytes, expectedBytes);
    }
}
