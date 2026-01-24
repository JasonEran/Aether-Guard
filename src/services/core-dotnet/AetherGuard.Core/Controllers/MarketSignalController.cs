using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace AetherGuard.Core.Controllers;

[ApiController]
[Route("api/v1/market")]
public class MarketSignalController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<MarketSignalController> _logger;

    public MarketSignalController(
        IConfiguration configuration,
        IWebHostEnvironment environment,
        ILogger<MarketSignalController> logger)
    {
        _configuration = configuration;
        _environment = environment;
        _logger = logger;
    }

    [HttpPost("signal")]
    [HttpPost("/market/signal")]
    public async Task<IActionResult> SetSignal([FromBody] MarketSignalRequest request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest(new { error = "Signal payload is required." });
        }

        var rebalanceSignal = request.RebalanceSignal;
        var timestamp = request.Timestamp > 0
            ? request.Timestamp
            : DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var payload = new
        {
            rebalanceSignal,
            timestamp
        };

        var marketSignalPath = _configuration["MarketSignalPath"] ?? "Data/market_signal.json";
        var resolvedPath = Path.IsPathRooted(marketSignalPath)
            ? marketSignalPath
            : Path.Combine(_environment.ContentRootPath, marketSignalPath);

        try
        {
            var directory = Path.GetDirectoryName(resolvedPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var json = JsonSerializer.Serialize(payload);
            await System.IO.File.WriteAllTextAsync(resolvedPath, json, cancellationToken);

            _logger.LogInformation("Market signal updated: rebalance={RebalanceSignal}", rebalanceSignal);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write market signal file.");
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Failed to persist signal." });
        }

        return Accepted(new { status = "updated", rebalanceSignal, timestamp });
    }

    public sealed class MarketSignalRequest
    {
        public bool RebalanceSignal { get; set; }
        public long Timestamp { get; set; }
    }
}
