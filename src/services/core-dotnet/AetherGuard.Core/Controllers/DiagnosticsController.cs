using System.Security.Cryptography;
using System.Text;
using AetherGuard.Core.Services;
using Microsoft.AspNetCore.Mvc;

namespace AetherGuard.Core.Controllers;

[ApiController]
[Route("api/v1/diagnostics")]
public sealed class DiagnosticsController : ControllerBase
{
    private readonly DiagnosticsBundleService _bundleService;
    private readonly ILogger<DiagnosticsController> _logger;
    private readonly string? _diagnosticsApiKey;

    public DiagnosticsController(
        DiagnosticsBundleService bundleService,
        IConfiguration configuration,
        ILogger<DiagnosticsController> logger)
    {
        _bundleService = bundleService;
        _logger = logger;
        _diagnosticsApiKey = configuration["Security:DiagnosticsApiKey"]
            ?? configuration["Security:CommandApiKey"];
    }

    [HttpGet("bundle")]
    public async Task<IActionResult> DownloadBundle(
        [FromQuery] bool includeSnapshots = true,
        [FromQuery] int maxSnapshots = 10,
        [FromQuery] long maxSnapshotBytes = 50 * 1024 * 1024,
        [FromQuery] long maxTotalSnapshotBytes = 200 * 1024 * 1024,
        [FromQuery] int telemetryLimit = 200,
        [FromQuery] int auditLimit = 200,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(_diagnosticsApiKey))
        {
            _logger.LogError("Security:DiagnosticsApiKey is not configured.");
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Diagnostics API key not configured." });
        }

        if (!Request.Headers.TryGetValue("X-API-Key", out var providedKey))
        {
            return Unauthorized(new { error = "Missing API key." });
        }

        if (!FixedTimeEquals(providedKey.ToString(), _diagnosticsApiKey))
        {
            return Unauthorized(new { error = "Invalid API key." });
        }

        var options = new DiagnosticsBundleOptions
        {
            IncludeSnapshots = includeSnapshots,
            MaxSnapshots = Math.Clamp(maxSnapshots, 0, 200),
            MaxSnapshotBytes = Math.Clamp(maxSnapshotBytes, 1 * 1024 * 1024, 1024L * 1024 * 1024),
            MaxTotalSnapshotBytes = Math.Clamp(maxTotalSnapshotBytes, 10 * 1024 * 1024, 5L * 1024 * 1024 * 1024),
            TelemetryLimit = Math.Clamp(telemetryLimit, 0, 1000),
            AuditLimit = Math.Clamp(auditLimit, 0, 1000)
        };

        DiagnosticsBundle bundle;
        try
        {
            bundle = await _bundleService.CreateBundleAsync(options, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Diagnostics bundle export failed.");
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Diagnostics bundle export failed." });
        }

        HttpContext.Response.OnCompleted(() =>
        {
            try
            {
                if (System.IO.File.Exists(bundle.FilePath))
                {
                    System.IO.File.Delete(bundle.FilePath);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to remove diagnostics bundle {BundlePath}", bundle.FilePath);
            }

            return Task.CompletedTask;
        });

        return PhysicalFile(bundle.FilePath, "application/zip", bundle.FileName);
    }

    private static bool FixedTimeEquals(string left, string right)
    {
        var leftBytes = Encoding.UTF8.GetBytes(left);
        var rightBytes = Encoding.UTF8.GetBytes(right);

        return leftBytes.Length == rightBytes.Length
            && CryptographicOperations.FixedTimeEquals(leftBytes, rightBytes);
    }
}
