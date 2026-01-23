using System.IO;
using System.Linq;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace AetherGuard.Core.Controllers;

[ApiController]
[Route("api/v1/artifacts")]
public class ArtifactController : ControllerBase
{
    private readonly ILogger<ArtifactController> _logger;
    private readonly IWebHostEnvironment _environment;
    private readonly string _storagePath;

    public ArtifactController(
        IConfiguration configuration,
        IWebHostEnvironment environment,
        ILogger<ArtifactController> logger)
    {
        _logger = logger;
        _environment = environment;
        _storagePath = configuration["StoragePath"] ?? "Data/Snapshots";
    }

    [HttpPost("upload/{workloadId}")]
    [HttpPost("/upload/{workloadId}")]
    public async Task<IActionResult> Upload(
        string workloadId,
        [FromForm] IFormFile file,
        CancellationToken cancellationToken)
    {
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

        var storageRoot = ResolveStorageRoot();
        var workloadDir = Path.Combine(storageRoot, safeWorkloadId);
        Directory.CreateDirectory(workloadDir);

        var timestamp = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff");
        var fileName = $"{timestamp}.tar.gz";
        var filePath = Path.Combine(workloadDir, fileName);

        await using (var stream = new FileStream(
            filePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            81920,
            useAsync: true))
        {
            await file.CopyToAsync(stream, cancellationToken);
        }

        _logger.LogInformation("Snapshot received, size: {Size}", file.Length);

        return Ok(new { status = "stored", file = fileName });
    }

    [HttpGet("download/{workloadId}")]
    [HttpGet("/download/{workloadId}")]
    public IActionResult Download(string workloadId)
    {
        if (string.IsNullOrWhiteSpace(workloadId))
        {
            return BadRequest(new { error = "WorkloadId is required." });
        }

        var safeWorkloadId = Path.GetFileName(workloadId.Trim());
        if (!string.Equals(safeWorkloadId, workloadId.Trim(), StringComparison.Ordinal))
        {
            return BadRequest(new { error = "Invalid workload identifier." });
        }

        var workloadDir = Path.Combine(ResolveStorageRoot(), safeWorkloadId);
        if (!Directory.Exists(workloadDir))
        {
            return NotFound(new { error = "Snapshot not found." });
        }

        var latestFile = new DirectoryInfo(workloadDir)
            .GetFiles("*.tar.gz")
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .FirstOrDefault();

        if (latestFile is null)
        {
            return NotFound(new { error = "Snapshot not found." });
        }

        return PhysicalFile(
            latestFile.FullName,
            "application/gzip",
            latestFile.Name,
            enableRangeProcessing: true);
    }

    private string ResolveStorageRoot()
    {
        if (Path.IsPathRooted(_storagePath))
        {
            return _storagePath;
        }

        return Path.Combine(_environment.ContentRootPath, _storagePath);
    }
}
