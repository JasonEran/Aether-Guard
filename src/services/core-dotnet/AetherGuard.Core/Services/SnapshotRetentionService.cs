using System.Collections.Concurrent;

namespace AetherGuard.Core.Services;

public sealed class SnapshotRetentionService : BackgroundService
{
    private readonly SnapshotStorageService _snapshotStorage;
    private readonly IConfiguration _configuration;
    private readonly ILogger<SnapshotRetentionService> _logger;
    private readonly SnapshotRetentionOptions _options;

    public SnapshotRetentionService(
        SnapshotStorageService snapshotStorage,
        IConfiguration configuration,
        ILogger<SnapshotRetentionService> logger)
    {
        _snapshotStorage = snapshotStorage;
        _configuration = configuration;
        _logger = logger;

        _options = configuration.GetSection("SnapshotRetention").Get<SnapshotRetentionOptions>()
            ?? new SnapshotRetentionOptions();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("Snapshot retention is disabled.");
            return;
        }

        if (_snapshotStorage.UsesS3 && _options.ApplyS3Lifecycle)
        {
            await _snapshotStorage.TryApplyS3LifecycleAsync(
                _options.S3ExpirationDays,
                stoppingToken);
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RunSweepAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Snapshot retention sweep failed.");
            }

            await Task.Delay(GetInterval(), stoppingToken);
        }
    }

    private TimeSpan GetInterval()
    {
        if (_options.SweepIntervalMinutes > 0)
        {
            return TimeSpan.FromMinutes(_options.SweepIntervalMinutes);
        }

        return TimeSpan.FromHours(1);
    }

    private async Task RunSweepAsync(CancellationToken cancellationToken)
    {
        var scanLimit = Math.Clamp(_options.ScanLimit, 50, 10000);
        var snapshots = await _snapshotStorage.ListSnapshotsAsync(scanLimit, cancellationToken);
        if (snapshots.Count == 0)
        {
            return;
        }

        var deletions = new ConcurrentDictionary<SnapshotDescriptor, string>(SnapshotDescriptorComparer.Instance);
        var now = DateTimeOffset.UtcNow;

        if (_options.MaxAgeDays > 0)
        {
            var cutoff = now.AddDays(-_options.MaxAgeDays);
            foreach (var snapshot in snapshots.Where(snapshot => snapshot.LastModifiedUtc < cutoff))
            {
                deletions.TryAdd(snapshot, "age");
            }
        }

        if (_options.MaxSnapshotsPerWorkload > 0)
        {
            foreach (var group in snapshots.GroupBy(snapshot => snapshot.WorkloadId))
            {
                var ordered = group.OrderByDescending(snapshot => snapshot.LastModifiedUtc).ToList();
                foreach (var snapshot in ordered.Skip(_options.MaxSnapshotsPerWorkload))
                {
                    deletions.TryAdd(snapshot, "per-workload-limit");
                }
            }
        }

        if (_options.MaxTotalSnapshots > 0 && snapshots.Count > _options.MaxTotalSnapshots)
        {
            var ordered = snapshots
                .OrderByDescending(snapshot => snapshot.LastModifiedUtc)
                .ToList();
            foreach (var snapshot in ordered.Skip(_options.MaxTotalSnapshots))
            {
                deletions.TryAdd(snapshot, "total-limit");
            }
        }

        if (deletions.IsEmpty)
        {
            return;
        }

        var deletedCount = 0;
        foreach (var entry in deletions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = entry.Key;
            var reason = entry.Value;

            try
            {
                var deleted = await _snapshotStorage.DeleteSnapshotAsync(snapshot, cancellationToken);
                if (deleted)
                {
                    deletedCount += 1;
                    _logger.LogInformation(
                        "Snapshot retention deleted {WorkloadId}/{FileName} ({Reason}).",
                        snapshot.WorkloadId,
                        snapshot.FileName,
                        reason);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Failed to delete snapshot {WorkloadId}/{FileName} during retention sweep.",
                    snapshot.WorkloadId,
                    snapshot.FileName);
            }
        }

        if (deletedCount > 0)
        {
            _logger.LogInformation("Snapshot retention sweep removed {Count} snapshots.", deletedCount);
        }
    }

    private sealed class SnapshotDescriptorComparer : IEqualityComparer<SnapshotDescriptor>
    {
        public static readonly SnapshotDescriptorComparer Instance = new();

        public bool Equals(SnapshotDescriptor? x, SnapshotDescriptor? y)
        {
            if (ReferenceEquals(x, y))
            {
                return true;
            }

            if (x is null || y is null)
            {
                return false;
            }

            return string.Equals(x.WorkloadId, y.WorkloadId, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.FileName, y.FileName, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.ObjectKey, y.ObjectKey, StringComparison.OrdinalIgnoreCase)
                && string.Equals(x.LocalPath, y.LocalPath, StringComparison.OrdinalIgnoreCase);
        }

        public int GetHashCode(SnapshotDescriptor obj)
        {
            return HashCode.Combine(
                obj.WorkloadId.ToLowerInvariant(),
                obj.FileName.ToLowerInvariant(),
                obj.ObjectKey?.ToLowerInvariant(),
                obj.LocalPath?.ToLowerInvariant());
        }
    }
}
