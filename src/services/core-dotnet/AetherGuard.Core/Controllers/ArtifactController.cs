using System.IO;
using System.Security.Cryptography;
using AetherGuard.Core.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AetherGuard.Core.Controllers;

[ApiController]
[Route("api/v1/artifacts")]
public class ArtifactController : ControllerBase
{
    private const string ApiKeyHeader = "X-API-Key";
    private readonly ILogger<ArtifactController> _logger;
    private readonly SnapshotStorageService _snapshotStorage;
    private readonly string? _artifactApiKey;

    public ArtifactController(
        SnapshotStorageService snapshotStorage,
        ILogger<ArtifactController> logger,
        IConfiguration configuration)
    {
        _logger = logger;
        _snapshotStorage = snapshotStorage;
        _artifactApiKey = configuration["Security:ArtifactApiKey"]
            ?? configuration["Security:CommandApiKey"];
    }

    [HttpPost("upload/{workloadId}")]
    [HttpPost("/upload/{workloadId}")]
    public async Task<IActionResult> Upload(
        string workloadId,
        [FromForm] IFormFile file,
        CancellationToken cancellationToken)
    {
        if (!TryAuthorize(out var failure))
        {
            return failure;
        }

        if (string.IsNullOrWhiteSpace(workloadId))
        {
            return BadRequest(new { error = "WorkloadId is required." });
        }

        if (file is null || file.Length == 0)
        {
            return BadRequest(new { error = "Snapshot file is required." });
        }

        var safeWorkloadId = Path.GetFileName(workloadId.Trim());
        if (!string.Equals(safeWorkloadId, workloadId.Trim(), StringComparison.Ordinal))
        {
            return BadRequest(new { error = "Invalid workload identifier." });
        }

        string? storedName;
        try
        {
            storedName = await _snapshotStorage.StoreSnapshotAsync(
                safeWorkloadId,
                file,
                cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Snapshot storage failed for {WorkloadId}.", safeWorkloadId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Snapshot storage failed." });
        }

        if (string.IsNullOrWhiteSpace(storedName))
        {
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Snapshot storage failed." });
        }

        _logger.LogInformation("Snapshot received, size: {Size}", file.Length);

        return Ok(new { status = "stored", file = storedName });
    }

    [HttpGet("download/{workloadId}")]
    [HttpGet("/download/{workloadId}")]
    public async Task<IActionResult> Download(string workloadId, CancellationToken cancellationToken)
    {
        if (!TryAuthorize(out var failure))
        {
            return failure;
        }

        if (string.IsNullOrWhiteSpace(workloadId))
        {
            return BadRequest(new { error = "WorkloadId is required." });
        }

        var safeWorkloadId = Path.GetFileName(workloadId.Trim());
        if (!string.Equals(safeWorkloadId, workloadId.Trim(), StringComparison.Ordinal))
        {
            return BadRequest(new { error = "Invalid workload identifier." });
        }

        SnapshotDownload? snapshot;
        try
        {
            snapshot = await _snapshotStorage.OpenLatestSnapshotAsync(safeWorkloadId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Snapshot download failed for {WorkloadId}.", safeWorkloadId);
            return StatusCode(StatusCodes.Status500InternalServerError, new { error = "Snapshot download failed." });
        }

        if (snapshot is null)
        {
            return NotFound(new { error = "Snapshot not found." });
        }

        if (snapshot.Disposable is not null)
        {
            HttpContext.Response.RegisterForDispose(snapshot.Disposable);
        }

        return File(
            snapshot.Stream,
            "application/gzip",
            snapshot.FileName,
            enableRangeProcessing: true);
    }

    private bool TryAuthorize(out IActionResult failure)
    {
        if (string.IsNullOrWhiteSpace(_artifactApiKey))
        {
            _logger.LogError("Security:ArtifactApiKey is not configured.");
            failure = StatusCode(StatusCodes.Status500InternalServerError, new { error = "Artifact API key not configured." });
            return false;
        }

        if (!Request.Headers.TryGetValue(ApiKeyHeader, out var providedKey))
        {
            failure = Unauthorized(new { error = "Missing API key." });
            return false;
        }

        if (!FixedTimeEquals(providedKey.ToString(), _artifactApiKey))
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
