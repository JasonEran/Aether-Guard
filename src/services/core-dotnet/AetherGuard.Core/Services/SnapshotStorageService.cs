using System.Globalization;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Util;
using Microsoft.AspNetCore.Http;

namespace AetherGuard.Core.Services;

public sealed class SnapshotStorageSettings
{
    public string Provider { get; set; } = "Local";
    public string LocalPath { get; set; } = string.Empty;
    public S3Settings S3 { get; set; } = new();
}

public sealed class S3Settings
{
    public string Bucket { get; set; } = string.Empty;
    public string Region { get; set; } = "us-east-1";
    public string Endpoint { get; set; } = string.Empty;
    public string AccessKey { get; set; } = string.Empty;
    public string SecretKey { get; set; } = string.Empty;
    public bool UsePathStyle { get; set; } = true;
    public string Prefix { get; set; } = "snapshots";
}

public sealed record SnapshotDownload(Stream Stream, string FileName, IDisposable? Disposable);

public sealed record SnapshotDescriptor(
    string WorkloadId,
    string FileName,
    long SizeBytes,
    DateTimeOffset LastModifiedUtc,
    string? LocalPath,
    string? ObjectKey);

public sealed class SnapshotStorageService
{
    private const string DefaultLocalPath = "Data/Snapshots";
    private const string DefaultPrefix = "snapshots";

    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<SnapshotStorageService> _logger;
    private readonly SnapshotStorageSettings _settings;
    private readonly IAmazonS3? _s3Client;
    private readonly SemaphoreSlim _bucketLock = new(1, 1);
    private bool _bucketReady;

    public SnapshotStorageService(
        IConfiguration configuration,
        IWebHostEnvironment environment,
        ILogger<SnapshotStorageService> logger)
    {
        _environment = environment;
        _logger = logger;

        _settings = new SnapshotStorageSettings();
        configuration.GetSection("SnapshotStorage").Bind(_settings);

        if (string.IsNullOrWhiteSpace(_settings.LocalPath))
        {
            _settings.LocalPath = configuration["StoragePath"] ?? DefaultLocalPath;
        }

        if (string.IsNullOrWhiteSpace(_settings.Provider))
        {
            _settings.Provider = "Local";
        }

        if (string.IsNullOrWhiteSpace(_settings.S3.Prefix))
        {
            _settings.S3.Prefix = DefaultPrefix;
        }

        if (ShouldUseS3(_settings))
        {
            _s3Client = BuildS3Client(_settings.S3);
        }

        if (UsesS3)
        {
            _logger.LogInformation("Snapshot storage configured for S3 bucket {Bucket}.", _settings.S3.Bucket);
        }
        else
        {
            _logger.LogInformation("Snapshot storage configured for local path {Path}.", ResolveLocalStorageRoot());
        }
    }

    public bool UsesS3 => _s3Client is not null;
    public string LocalStorageRoot => ResolveLocalStorageRoot();

    public async Task<string?> StoreSnapshotAsync(
        string workloadId,
        IFormFile file,
        CancellationToken cancellationToken)
    {
        var safeWorkloadId = SanitizeWorkloadId(workloadId);
        if (string.IsNullOrWhiteSpace(safeWorkloadId))
        {
            return null;
        }

        var fileName = $"{DateTime.UtcNow.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture)}.tar.gz";

        if (UsesS3)
        {
            await EnsureBucketReadyAsync(cancellationToken);
            await using var stream = file.OpenReadStream();
            var key = BuildObjectKey(safeWorkloadId, fileName);
            var request = new PutObjectRequest
            {
                BucketName = _settings.S3.Bucket,
                Key = key,
                InputStream = stream,
                ContentType = "application/gzip"
            };
            await _s3Client!.PutObjectAsync(request, cancellationToken);
            return fileName;
        }

        var storageRoot = ResolveLocalStorageRoot();
        var workloadDir = Path.Combine(storageRoot, safeWorkloadId);
        Directory.CreateDirectory(workloadDir);

        var filePath = Path.Combine(workloadDir, fileName);
        await using (var output = new FileStream(
            filePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            81920,
            useAsync: true))
        {
            await file.CopyToAsync(output, cancellationToken);
        }

        return fileName;
    }

    public async Task<SnapshotDownload?> OpenLatestSnapshotAsync(
        string workloadId,
        CancellationToken cancellationToken)
    {
        var safeWorkloadId = SanitizeWorkloadId(workloadId);
        if (string.IsNullOrWhiteSpace(safeWorkloadId))
        {
            return null;
        }

        if (UsesS3)
        {
            var latestObject = await FindLatestObjectAsync(safeWorkloadId, cancellationToken);
            if (latestObject is null)
            {
                return null;
            }

            var response = await _s3Client!.GetObjectAsync(
                _settings.S3.Bucket,
                latestObject.Key,
                cancellationToken);

            return new SnapshotDownload(
                response.ResponseStream,
                Path.GetFileName(latestObject.Key),
                response);
        }

        var latestFile = FindLatestLocalFile(safeWorkloadId);
        if (latestFile is null)
        {
            return null;
        }

        var stream = new FileStream(
            latestFile.FullName,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            81920,
            useAsync: true);

        return new SnapshotDownload(stream, latestFile.Name, null);
    }

    public async Task<SnapshotDownload?> OpenSnapshotAsync(
        SnapshotDescriptor snapshot,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(snapshot.LocalPath))
        {
            if (!File.Exists(snapshot.LocalPath))
            {
                return null;
            }

            var stream = new FileStream(
                snapshot.LocalPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                81920,
                useAsync: true);
            return new SnapshotDownload(stream, snapshot.FileName, null);
        }

        if (UsesS3 && !string.IsNullOrWhiteSpace(snapshot.ObjectKey))
        {
            var response = await _s3Client!.GetObjectAsync(
                _settings.S3.Bucket,
                snapshot.ObjectKey,
                cancellationToken);
            return new SnapshotDownload(
                response.ResponseStream,
                Path.GetFileName(snapshot.ObjectKey),
                response);
        }

        return null;
    }

    public async Task<IReadOnlyList<SnapshotDescriptor>> ListSnapshotsAsync(
        int maxEntries,
        CancellationToken cancellationToken)
    {
        if (maxEntries <= 0)
        {
            return [];
        }

        var clampedMax = Math.Clamp(maxEntries, 1, 10000);
        var snapshots = new List<SnapshotDescriptor>();

        if (UsesS3)
        {
            var prefix = _settings.S3.Prefix.Trim('/');
            var basePrefix = string.IsNullOrEmpty(prefix) ? string.Empty : $"{prefix}/";
            var request = new ListObjectsV2Request
            {
                BucketName = _settings.S3.Bucket,
                Prefix = basePrefix
            };

            ListObjectsV2Response response;
            do
            {
                response = await _s3Client!.ListObjectsV2Async(request, cancellationToken);
                foreach (var item in response.S3Objects)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!item.Key.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var trimmedKey = item.Key;
                    if (!string.IsNullOrEmpty(basePrefix)
                        && trimmedKey.StartsWith(basePrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        trimmedKey = trimmedKey[basePrefix.Length..];
                    }

                    var parts = trimmedKey.Split('/', 2, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length < 2)
                    {
                        continue;
                    }

                    var workloadId = parts[0];
                    var fileName = Path.GetFileName(parts[1]);
                    snapshots.Add(new SnapshotDescriptor(
                        workloadId,
                        fileName,
                        item.Size,
                        new DateTimeOffset(item.LastModified),
                        null,
                        item.Key));
                }

                if (snapshots.Count > clampedMax * 2)
                {
                    snapshots = snapshots
                        .OrderByDescending(item => item.LastModifiedUtc)
                        .Take(clampedMax)
                        .ToList();
                }

                request.ContinuationToken = response.NextContinuationToken;
            } while (response.IsTruncated);

            return snapshots
                .OrderByDescending(item => item.LastModifiedUtc)
                .Take(clampedMax)
                .ToList();
        }

        var storageRoot = ResolveLocalStorageRoot();
        if (!Directory.Exists(storageRoot))
        {
            return [];
        }

        var rootDir = new DirectoryInfo(storageRoot);
        foreach (var workloadDir in rootDir.EnumerateDirectories())
        {
            foreach (var file in workloadDir.EnumerateFiles("*.tar.gz"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                snapshots.Add(new SnapshotDescriptor(
                    workloadDir.Name,
                    file.Name,
                    file.Length,
                    new DateTimeOffset(file.LastWriteTimeUtc),
                    file.FullName,
                    null));
            }
        }

        return snapshots
            .OrderByDescending(item => item.LastModifiedUtc)
            .Take(clampedMax)
            .ToList();
    }

    public async Task<bool> DeleteSnapshotAsync(SnapshotDescriptor snapshot, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(snapshot.LocalPath))
        {
            if (!File.Exists(snapshot.LocalPath))
            {
                return false;
            }

            File.Delete(snapshot.LocalPath);
            return true;
        }

        if (UsesS3 && !string.IsNullOrWhiteSpace(snapshot.ObjectKey))
        {
            await _s3Client!.DeleteObjectAsync(
                _settings.S3.Bucket,
                snapshot.ObjectKey,
                cancellationToken);
            return true;
        }

        return false;
    }

    public async Task TryApplyS3LifecycleAsync(int expirationDays, CancellationToken cancellationToken)
    {
        if (!UsesS3 || expirationDays <= 0)
        {
            return;
        }

        await EnsureBucketReadyAsync(cancellationToken);

        var prefix = _settings.S3.Prefix.Trim('/');
        var rule = new LifecycleRule
        {
            Id = "aether-guard-snapshot-expiration",
            Status = LifecycleRuleStatus.Enabled,
            Filter = string.IsNullOrEmpty(prefix)
                ? new LifecycleFilter()
                : new LifecycleFilter
                {
                    LifecycleFilterPredicate = new LifecyclePrefixPredicate { Prefix = prefix + "/" }
                },
            Expiration = new LifecycleRuleExpiration { Days = expirationDays }
        };

        var request = new PutLifecycleConfigurationRequest
        {
            BucketName = _settings.S3.Bucket,
            Configuration = new LifecycleConfiguration { Rules = [rule] }
        };

        try
        {
            await _s3Client!.PutLifecycleConfigurationAsync(request, cancellationToken);
            _logger.LogInformation("Applied S3 lifecycle expiration of {Days} days.", expirationDays);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to apply S3 lifecycle configuration.");
        }
    }

    public async Task<bool> HasSnapshotAsync(string workloadId, CancellationToken cancellationToken)
    {
        var safeWorkloadId = SanitizeWorkloadId(workloadId);
        if (string.IsNullOrWhiteSpace(safeWorkloadId))
        {
            return false;
        }

        if (UsesS3)
        {
            var latestObject = await FindLatestObjectAsync(safeWorkloadId, cancellationToken);
            return latestObject is not null;
        }

        return FindLatestLocalFile(safeWorkloadId) is not null;
    }

    private string ResolveLocalStorageRoot()
    {
        if (Path.IsPathRooted(_settings.LocalPath))
        {
            return _settings.LocalPath;
        }

        return Path.Combine(_environment.ContentRootPath, _settings.LocalPath);
    }

    private static string SanitizeWorkloadId(string workloadId)
    {
        if (string.IsNullOrWhiteSpace(workloadId))
        {
            return string.Empty;
        }

        return Path.GetFileName(workloadId.Trim());
    }

    private string BuildObjectKey(string workloadId, string fileName)
    {
        var prefix = _settings.S3.Prefix.Trim('/');
        return string.IsNullOrEmpty(prefix)
            ? $"{workloadId}/{fileName}"
            : $"{prefix}/{workloadId}/{fileName}";
    }

    private async Task EnsureBucketReadyAsync(CancellationToken cancellationToken)
    {
        if (_bucketReady)
        {
            return;
        }

        await _bucketLock.WaitAsync(cancellationToken);
        try
        {
            if (_bucketReady)
            {
                return;
            }

            var exists = await AmazonS3Util.DoesS3BucketExistV2Async(
                _s3Client!,
                _settings.S3.Bucket);
            if (!exists)
            {
                _logger.LogInformation("Creating snapshot bucket {Bucket}.", _settings.S3.Bucket);
                await _s3Client!.PutBucketAsync(
                    new PutBucketRequest { BucketName = _settings.S3.Bucket },
                    cancellationToken);
            }

            _bucketReady = true;
        }
        finally
        {
            _bucketLock.Release();
        }
    }

    private async Task<S3Object?> FindLatestObjectAsync(
        string workloadId,
        CancellationToken cancellationToken)
    {
        var prefix = BuildObjectKey(workloadId, string.Empty);
        if (!prefix.EndsWith("/"))
        {
            prefix += "/";
        }

        S3Object? latest = null;
        var request = new ListObjectsV2Request
        {
            BucketName = _settings.S3.Bucket,
            Prefix = prefix
        };

        ListObjectsV2Response response;
        do
        {
            response = await _s3Client!.ListObjectsV2Async(request, cancellationToken);
            foreach (var item in response.S3Objects)
            {
                if (!item.Key.EndsWith(".tar.gz", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (latest is null || item.LastModified > latest.LastModified)
                {
                    latest = item;
                }
            }

            request.ContinuationToken = response.NextContinuationToken;
        } while (response.IsTruncated);

        return latest;
    }

    private FileInfo? FindLatestLocalFile(string workloadId)
    {
        var workloadDir = Path.Combine(ResolveLocalStorageRoot(), workloadId);
        if (!Directory.Exists(workloadDir))
        {
            return null;
        }

        return new DirectoryInfo(workloadDir)
            .GetFiles("*.tar.gz")
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .FirstOrDefault();
    }

    private static bool ShouldUseS3(SnapshotStorageSettings settings)
    {
        if (string.Equals(settings.Provider, "S3", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(settings.S3.Bucket);
    }

    private IAmazonS3 BuildS3Client(S3Settings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.Bucket))
        {
            throw new InvalidOperationException("SnapshotStorage:S3:Bucket is required when using S3.");
        }

        if (string.IsNullOrWhiteSpace(settings.AccessKey) || string.IsNullOrWhiteSpace(settings.SecretKey))
        {
            throw new InvalidOperationException("SnapshotStorage:S3:AccessKey and SecretKey are required when using S3.");
        }

        var regionName = string.IsNullOrWhiteSpace(settings.Region) ? "us-east-1" : settings.Region;
        var config = new AmazonS3Config
        {
            RegionEndpoint = RegionEndpoint.GetBySystemName(regionName)
        };

        if (!string.IsNullOrWhiteSpace(settings.Endpoint))
        {
            if (!Uri.TryCreate(settings.Endpoint, UriKind.Absolute, out var endpointUri))
            {
                throw new InvalidOperationException("SnapshotStorage:S3:Endpoint must be a valid URI.");
            }

            config.ServiceURL = settings.Endpoint;
            config.ForcePathStyle = settings.UsePathStyle;
            config.UseHttp = string.Equals(endpointUri.Scheme, "http", StringComparison.OrdinalIgnoreCase);
        }

        var credentials = new BasicAWSCredentials(settings.AccessKey, settings.SecretKey);
        return new AmazonS3Client(credentials, config);
    }
}
