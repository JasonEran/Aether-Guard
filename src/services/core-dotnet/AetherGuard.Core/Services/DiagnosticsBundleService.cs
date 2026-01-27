using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using AetherGuard.Core.Data;
using AetherGuard.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace AetherGuard.Core.Services;

public sealed record DiagnosticsBundle(string FilePath, string FileName);

public sealed record DiagnosticsBundleOptions
{
    public bool IncludeSnapshots { get; init; } = true;
    public int MaxSnapshots { get; init; } = 10;
    public long MaxSnapshotBytes { get; init; } = 50 * 1024 * 1024;
    public long MaxTotalSnapshotBytes { get; init; } = 200 * 1024 * 1024;
    public int TelemetryLimit { get; init; } = 200;
    public int AuditLimit { get; init; } = 200;
}

public sealed record SnapshotReportEntry(
    string WorkloadId,
    string FileName,
    long SizeBytes,
    DateTimeOffset LastModifiedUtc,
    string Location,
    bool Included,
    string? Reason);

public sealed record SnapshotReportSummary(
    string Provider,
    string? Location,
    bool IncludeSnapshots,
    int MaxSnapshots,
    long MaxSnapshotBytes,
    long MaxTotalSnapshotBytes,
    int TotalSnapshots,
    int IncludedSnapshots,
    long TotalSnapshotBytes,
    long IncludedSnapshotBytes);

public sealed record DiagnosticsReport(
    DateTimeOffset GeneratedAtUtc,
    string Environment,
    string ApplicationVersion,
    string MachineName,
    string OsDescription,
    string FrameworkDescription,
    int ProcessId,
    double UptimeSeconds,
    SnapshotReportSummary SnapshotSummary,
    IReadOnlyList<SnapshotReportEntry> Snapshots,
    int TelemetryRecords,
    int AuditRecords,
    IReadOnlyList<string> Warnings);

public sealed class DiagnosticsBundleService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly JsonSerializerOptions RawJsonOptions = new()
    {
        WriteIndented = true
    };

    private static readonly string[] SensitiveKeyFragments =
    [
        "password",
        "secret",
        "apikey",
        "accesskey",
        "token",
        "connectionstring"
    ];

    private readonly ApplicationDbContext _context;
    private readonly SnapshotStorageService _snapshotStorage;
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<DiagnosticsBundleService> _logger;

    public DiagnosticsBundleService(
        ApplicationDbContext context,
        SnapshotStorageService snapshotStorage,
        IConfiguration configuration,
        IWebHostEnvironment environment,
        ILogger<DiagnosticsBundleService> logger)
    {
        _context = context;
        _snapshotStorage = snapshotStorage;
        _configuration = configuration;
        _environment = environment;
        _logger = logger;
    }

    public async Task<DiagnosticsBundle> CreateBundleAsync(
        DiagnosticsBundleOptions options,
        CancellationToken cancellationToken)
    {
        var warnings = new List<string>();
        var now = DateTimeOffset.UtcNow;
        var bundleName = $"aetherguard-diagnostics-{now:yyyyMMdd-HHmmss}.zip";
        var tempPath = Path.Combine(
            Path.GetTempPath(),
            $"aetherguard-diagnostics-{now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.zip");

        var telemetry = await FetchTelemetryAsync(options.TelemetryLimit, cancellationToken);
        var audits = await FetchAuditsAsync(options.AuditLimit, cancellationToken);

        IReadOnlyList<SnapshotDescriptor> snapshots = Array.Empty<SnapshotDescriptor>();
        try
        {
            snapshots = await _snapshotStorage.ListSnapshotsAsync(options.MaxSnapshots, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enumerate snapshot metadata for diagnostics bundle.");
            warnings.Add("Snapshot enumeration failed. Check snapshot storage connectivity.");
        }

        var snapshotReport = new List<SnapshotReportEntry>();
        var includedSnapshotBytes = 0L;
        var includedSnapshotCount = 0;
        var totalSnapshotBytes = snapshots.Sum(item => item.SizeBytes);

        try
        {
            await using var fileStream = new FileStream(
                tempPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                useAsync: true);
            using var archive = new ZipArchive(fileStream, ZipArchiveMode.Create, leaveOpen: false);

            await AddJsonEntryAsync(archive, "data/telemetry.json", telemetry, JsonOptions, cancellationToken);
            await AddJsonEntryAsync(archive, "data/audits.json", audits, JsonOptions, cancellationToken);

            await AddSanitizedConfigAsync(
                archive,
                "config/appsettings.json",
                Path.Combine(_environment.ContentRootPath, "appsettings.json"),
                warnings,
                cancellationToken);

            await AddSanitizedConfigAsync(
                archive,
                "config/appsettings.Development.json",
                Path.Combine(_environment.ContentRootPath, "appsettings.Development.json"),
                warnings,
                cancellationToken);

            await AddMarketSignalAsync(archive, warnings, cancellationToken);

            foreach (var snapshot in snapshots)
            {
                var location = GetSnapshotLocation(snapshot);
                if (!options.IncludeSnapshots)
                {
                    snapshotReport.Add(BuildSnapshotEntry(snapshot, location, false, "Snapshot inclusion disabled"));
                    continue;
                }

                if (snapshot.SizeBytes > options.MaxSnapshotBytes)
                {
                    snapshotReport.Add(BuildSnapshotEntry(snapshot, location, false, "Snapshot exceeds size limit"));
                    continue;
                }

                if (includedSnapshotBytes + snapshot.SizeBytes > options.MaxTotalSnapshotBytes)
                {
                    snapshotReport.Add(BuildSnapshotEntry(snapshot, location, false, "Bundle size limit reached"));
                    continue;
                }

                var entryName = $"snapshots/{SanitizePathSegment(snapshot.WorkloadId)}/{SanitizePathSegment(snapshot.FileName)}";
                var added = await TryAddSnapshotAsync(
                    archive,
                    entryName,
                    snapshot,
                    warnings,
                    cancellationToken);
                if (!added)
                {
                    snapshotReport.Add(BuildSnapshotEntry(snapshot, location, false, "Snapshot unavailable"));
                    continue;
                }

                includedSnapshotBytes += snapshot.SizeBytes;
                includedSnapshotCount += 1;
                snapshotReport.Add(BuildSnapshotEntry(snapshot, location, true, null));
            }

            var summary = BuildSnapshotSummary(
                options,
                snapshots.Count,
                includedSnapshotCount,
                totalSnapshotBytes,
                includedSnapshotBytes);

            var report = BuildReport(
                now,
                summary,
                snapshotReport,
                telemetry.Count,
                audits.Count,
                warnings);

            await AddJsonEntryAsync(archive, "diagnostics/report.json", report, JsonOptions, cancellationToken);

            return new DiagnosticsBundle(tempPath, bundleName);
        }
        catch
        {
            try
            {
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch
            {
                // Best effort cleanup; ignore failures.
            }

            throw;
        }
    }

    private async Task<List<TelemetryRecord>> FetchTelemetryAsync(int limit, CancellationToken cancellationToken)
    {
        if (limit <= 0)
        {
            return [];
        }

        return await _context.TelemetryRecords
            .AsNoTracking()
            .OrderByDescending(record => record.Timestamp)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    private async Task<List<CommandAudit>> FetchAuditsAsync(int limit, CancellationToken cancellationToken)
    {
        if (limit <= 0)
        {
            return [];
        }

        return await _context.CommandAudits
            .AsNoTracking()
            .OrderByDescending(audit => audit.CreatedAt)
            .Take(limit)
            .ToListAsync(cancellationToken);
    }

    private DiagnosticsReport BuildReport(
        DateTimeOffset createdAt,
        SnapshotReportSummary summary,
        IReadOnlyList<SnapshotReportEntry> snapshots,
        int telemetryCount,
        int auditCount,
        IReadOnlyList<string> warnings)
    {
        var process = Process.GetCurrentProcess();
        var uptime = createdAt - process.StartTime.ToUniversalTime();
        var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "unknown";

        return new DiagnosticsReport(
            createdAt,
            _environment.EnvironmentName ?? "unknown",
            version,
            Environment.MachineName,
            RuntimeInformation.OSDescription,
            RuntimeInformation.FrameworkDescription,
            Environment.ProcessId,
            uptime.TotalSeconds,
            summary,
            snapshots,
            telemetryCount,
            auditCount,
            warnings);
    }

    private SnapshotReportSummary BuildSnapshotSummary(
        DiagnosticsBundleOptions options,
        int totalSnapshots,
        int includedSnapshots,
        long totalSnapshotBytes,
        long includedSnapshotBytes)
    {
        var provider = _snapshotStorage.UsesS3 ? "S3" : "Local";
        var location = _snapshotStorage.UsesS3
            ? BuildS3Location()
            : _snapshotStorage.LocalStorageRoot;

        return new SnapshotReportSummary(
            provider,
            location,
            options.IncludeSnapshots,
            options.MaxSnapshots,
            options.MaxSnapshotBytes,
            options.MaxTotalSnapshotBytes,
            totalSnapshots,
            includedSnapshots,
            totalSnapshotBytes,
            includedSnapshotBytes);
    }

    private string? BuildS3Location()
    {
        var bucket = _configuration["SnapshotStorage:S3:Bucket"];
        if (string.IsNullOrWhiteSpace(bucket))
        {
            return null;
        }

        var prefix = _configuration["SnapshotStorage:S3:Prefix"];
        if (string.IsNullOrWhiteSpace(prefix))
        {
            return $"s3://{bucket}";
        }

        return $"s3://{bucket}/{prefix.Trim('/')}";
    }

    private async Task AddMarketSignalAsync(
        ZipArchive archive,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        var signalPath = _configuration["MarketSignalPath"] ?? "Data/market_signal.json";
        var resolvedPath = Path.IsPathRooted(signalPath)
            ? signalPath
            : Path.Combine(_environment.ContentRootPath, signalPath);

        if (!File.Exists(resolvedPath))
        {
            warnings.Add($"Market signal file missing: {resolvedPath}");
            return;
        }

        await AddFileEntryAsync(
            archive,
            "data/market_signal.json",
            resolvedPath,
            cancellationToken);
    }

    private async Task<bool> TryAddSnapshotAsync(
        ZipArchive archive,
        string entryName,
        SnapshotDescriptor snapshot,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        SnapshotDownload? download = null;
        try
        {
            download = await _snapshotStorage.OpenSnapshotAsync(snapshot, cancellationToken);
            if (download is null)
            {
                return false;
            }

            var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
            await using var entryStream = entry.Open();
            await download.Stream.CopyToAsync(entryStream, cancellationToken);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to include snapshot {SnapshotFile} in diagnostics bundle.", snapshot.FileName);
            warnings.Add($"Snapshot '{snapshot.FileName}' failed to include: {ex.Message}");
            return false;
        }
        finally
        {
            if (download?.Disposable is not null)
            {
                download.Disposable.Dispose();
            }

            if (download?.Stream is not null)
            {
                await download.Stream.DisposeAsync();
            }
        }
    }

    private SnapshotReportEntry BuildSnapshotEntry(
        SnapshotDescriptor snapshot,
        string location,
        bool included,
        string? reason)
        => new(
            snapshot.WorkloadId,
            snapshot.FileName,
            snapshot.SizeBytes,
            snapshot.LastModifiedUtc,
            location,
            included,
            reason);

    private string GetSnapshotLocation(SnapshotDescriptor snapshot)
    {
        if (!string.IsNullOrWhiteSpace(snapshot.LocalPath))
        {
            var root = _snapshotStorage.LocalStorageRoot;
            return Path.GetRelativePath(root, snapshot.LocalPath);
        }

        return snapshot.ObjectKey ?? snapshot.FileName;
    }

    private static string SanitizePathSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        var sanitized = value.Replace('\\', '/');
        sanitized = Path.GetFileName(sanitized);
        return string.IsNullOrWhiteSpace(sanitized) ? "unknown" : sanitized;
    }

    private static async Task AddFileEntryAsync(
        ZipArchive archive,
        string entryName,
        string filePath,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        await using var entryStream = entry.Open();
        await using var fileStream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            useAsync: true);
        await fileStream.CopyToAsync(entryStream, cancellationToken);
    }

    private static async Task AddJsonEntryAsync(
        ZipArchive archive,
        string entryName,
        object payload,
        JsonSerializerOptions options,
        CancellationToken cancellationToken)
    {
        var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);
        await using var entryStream = entry.Open();
        await JsonSerializer.SerializeAsync(entryStream, payload, options, cancellationToken);
    }

    private async Task AddSanitizedConfigAsync(
        ZipArchive archive,
        string entryName,
        string filePath,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        if (!File.Exists(filePath))
        {
            return;
        }

        JsonNode? node;
        try
        {
            var text = await File.ReadAllTextAsync(filePath, cancellationToken);
            node = JsonNode.Parse(text);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read config file {ConfigPath}", filePath);
            warnings.Add($"Failed to read config file {filePath}");
            return;
        }

        if (node is null)
        {
            return;
        }

        SanitizeNode(node);
        await AddJsonEntryAsync(archive, entryName, node, RawJsonOptions, cancellationToken);
    }

    private void SanitizeNode(JsonNode node)
    {
        if (node is JsonObject jsonObject)
        {
            var keys = jsonObject.Select(kvp => kvp.Key).ToList();
            foreach (var key in keys)
            {
                if (!jsonObject.TryGetPropertyValue(key, out var child) || child is null)
                {
                    continue;
                }

                if (string.Equals(key, "ConnectionStrings", StringComparison.OrdinalIgnoreCase))
                {
                    if (child is JsonObject connectionObject)
                    {
                        foreach (var connectionKey in connectionObject.Select(kvp => kvp.Key).ToList())
                        {
                            connectionObject[connectionKey] = "***redacted***";
                        }
                    }
                    else
                    {
                        jsonObject[key] = "***redacted***";
                    }

                    continue;
                }

                if (IsSensitiveKey(key))
                {
                    jsonObject[key] = "***redacted***";
                    continue;
                }

                SanitizeNode(child);
            }
        }
        else if (node is JsonArray array)
        {
            foreach (var item in array)
            {
                if (item is not null)
                {
                    SanitizeNode(item);
                }
            }
        }
    }

    private bool IsSensitiveKey(string key)
        => SensitiveKeyFragments.Any(fragment =>
            key.Contains(fragment, StringComparison.OrdinalIgnoreCase));
}
