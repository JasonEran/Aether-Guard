using System.Text.Json;
using AetherGuard.Core.Data;
using AetherGuard.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace AetherGuard.Core.Services;

public class MigrationOrchestrator
{
    private static readonly TimeSpan DefaultHeartbeatTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan DefaultCommandTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private readonly ApplicationDbContext _context;
    private readonly CommandService _commandService;
    private readonly AnalysisService _analysisService;
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<MigrationOrchestrator> _logger;

    public MigrationOrchestrator(
        ApplicationDbContext context,
        CommandService commandService,
        AnalysisService analysisService,
        IConfiguration configuration,
        IWebHostEnvironment environment,
        ILogger<MigrationOrchestrator> logger)
    {
        _context = context;
        _commandService = commandService;
        _analysisService = analysisService;
        _configuration = configuration;
        _environment = environment;
        _logger = logger;
    }

    public async Task RunMigrationCycle(string sourceAgentId, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(sourceAgentId, out var sourceId))
        {
            _logger.LogWarning("Invalid source agent id: {SourceAgentId}", sourceAgentId);
            return;
        }

        var sourceAgent = await _context.Agents
            .AsNoTracking()
            .FirstOrDefaultAsync(agent => agent.Id == sourceId, cancellationToken);

        if (sourceAgent is null || !IsAgentActive(sourceAgent))
        {
            _logger.LogInformation("Source agent {SourceAgentId} is not active.", sourceAgentId);
            return;
        }

        if (await HasPendingCommandsAsync(sourceId, cancellationToken))
        {
            _logger.LogInformation("Source agent {SourceAgentId} has pending commands. Skipping cycle.", sourceAgentId);
            return;
        }

        if (await HasRecentMigrationAsync(sourceAgentId, cancellationToken))
        {
            _logger.LogInformation("Recent migration found for {SourceAgentId}. Skipping cycle.", sourceAgentId);
            return;
        }

        var latestTelemetry = await _context.TelemetryRecords
            .AsNoTracking()
            .Where(record => record.AgentId == sourceAgentId)
            .OrderByDescending(record => record.Timestamp)
            .FirstOrDefaultAsync(cancellationToken);

        var rebalanceSignal = latestTelemetry?.RebalanceSignal ?? false;
        var diskAvailable = latestTelemetry?.DiskAvailable ?? 0;

        if (TryReadMarketSignal(out var overrideSignal))
        {
            rebalanceSignal = overrideSignal;
        }

        var riskPayload = new TelemetryPayload(
            sourceAgentId,
            DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            latestTelemetry?.WorkloadTier ?? "T2",
            rebalanceSignal,
            diskAvailable);

        var riskResult = await _analysisService.AnalyzeAsync(riskPayload);
        var isCritical = string.Equals(riskResult.Status, "CRITICAL", StringComparison.OrdinalIgnoreCase)
            || (rebalanceSignal && string.Equals(riskResult.Status, "Unavailable", StringComparison.OrdinalIgnoreCase));

        if (!isCritical)
        {
            _logger.LogInformation("Risk check for {SourceAgentId} returned {Status}", sourceAgentId, riskResult.Status);
            return;
        }

        var targetAgent = await FindIdleTargetAsync(sourceId, cancellationToken);
        if (targetAgent is null)
        {
            _logger.LogWarning("No idle target agent available for migration from {SourceAgentId}", sourceAgentId);
            return;
        }

        var checkpointCommand = await _commandService.QueueCommand(
            sourceAgentId,
            "CHECKPOINT",
            new { },
            cancellationToken);

        var checkpointResult = await WaitForCommandCompletionAsync(
            checkpointCommand.CommandId,
            DefaultCommandTimeout,
            cancellationToken);

        if (checkpointResult != CommandOutcome.Completed)
        {
            _logger.LogWarning("Checkpoint command {CommandId} failed or timed out.", checkpointCommand.CommandId);
            return;
        }

        var snapshotPath = FindLatestSnapshotPath(sourceAgentId);
        if (snapshotPath is null)
        {
            _logger.LogWarning("Snapshot artifact not found for {SourceAgentId}", sourceAgentId);
            return;
        }

        var downloadUrl = BuildDownloadUrl(sourceAgentId);
        var restoreCommand = await _commandService.QueueCommand(
            targetAgent.Id.ToString(),
            "RESTORE",
            new { snapshotUrl = downloadUrl },
            cancellationToken);

        var restoreResult = await WaitForCommandCompletionAsync(
            restoreCommand.CommandId,
            DefaultCommandTimeout,
            cancellationToken);

        if (restoreResult != CommandOutcome.Completed)
        {
            _logger.LogWarning("Restore command {CommandId} failed or timed out.", restoreCommand.CommandId);
            return;
        }

        _context.CommandAudits.Add(new CommandAudit
        {
            CommandId = restoreCommand.CommandId,
            Actor = sourceAgentId,
            Action = "Migration Completed",
            Result = targetAgent.Id.ToString(),
            Error = string.Empty,
            CreatedAt = DateTime.UtcNow
        });

        await _context.SaveChangesAsync(cancellationToken);
    }

    private bool IsAgentActive(Agent agent)
    {
        if (!string.Equals(agent.Status, "ONLINE", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var timeout = GetHeartbeatTimeout();
        return DateTimeOffset.UtcNow - agent.LastHeartbeat <= timeout;
    }

    private TimeSpan GetHeartbeatTimeout()
    {
        if (int.TryParse(_configuration["HeartbeatTimeoutSeconds"], out var seconds) && seconds > 0)
        {
            return TimeSpan.FromSeconds(seconds);
        }

        return DefaultHeartbeatTimeout;
    }

    private async Task<bool> HasPendingCommandsAsync(Guid agentId, CancellationToken cancellationToken)
    {
        return await _context.AgentCommands
            .AsNoTracking()
            .AnyAsync(command => command.AgentId == agentId && command.Status == "PENDING", cancellationToken);
    }

    private async Task<bool> HasRecentMigrationAsync(string sourceAgentId, CancellationToken cancellationToken)
    {
        var cutoff = DateTime.UtcNow.AddMinutes(-2);
        return await _context.CommandAudits
            .AsNoTracking()
            .AnyAsync(
                audit => audit.Action == "Migration Completed"
                    && audit.Actor == sourceAgentId
                    && audit.CreatedAt >= cutoff,
                cancellationToken);
    }

    private async Task<Agent?> FindIdleTargetAsync(Guid sourceAgentId, CancellationToken cancellationToken)
    {
        var candidates = await _context.Agents
            .AsNoTracking()
            .Where(agent => agent.Id != sourceAgentId)
            .ToListAsync(cancellationToken);

        foreach (var candidate in candidates)
        {
            if (!IsAgentActive(candidate))
            {
                continue;
            }

            if (await HasPendingCommandsAsync(candidate.Id, cancellationToken))
            {
                continue;
            }

            return candidate;
        }

        return null;
    }

    private async Task<CommandOutcome> WaitForCommandCompletionAsync(
        Guid commandId,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow <= deadline)
        {
            var status = await _context.AgentCommands
                .AsNoTracking()
                .Where(command => command.CommandId == commandId)
                .Select(command => command.Status)
                .FirstOrDefaultAsync(cancellationToken);

            if (string.Equals(status, "COMPLETED", StringComparison.OrdinalIgnoreCase))
            {
                return CommandOutcome.Completed;
            }

            if (string.Equals(status, "FAILED", StringComparison.OrdinalIgnoreCase))
            {
                return CommandOutcome.Failed;
            }

            await Task.Delay(PollInterval, cancellationToken);
        }

        return CommandOutcome.Timeout;
    }

    private bool TryReadMarketSignal(out bool rebalanceSignal)
    {
        rebalanceSignal = false;
        var marketSignalPath = _configuration["MarketSignalPath"] ?? "Data/market_signal.json";
        var resolvedPath = Path.IsPathRooted(marketSignalPath)
            ? marketSignalPath
            : Path.Combine(_environment.ContentRootPath, marketSignalPath);

        if (!File.Exists(resolvedPath))
        {
            return false;
        }

        try
        {
            using var stream = File.OpenRead(resolvedPath);
            var document = JsonDocument.Parse(stream);
            if (document.RootElement.TryGetProperty("rebalanceSignal", out var element)
                && (element.ValueKind == JsonValueKind.True || element.ValueKind == JsonValueKind.False))
            {
                rebalanceSignal = element.GetBoolean();
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read market signal file.");
        }

        return false;
    }

    private string? FindLatestSnapshotPath(string workloadId)
    {
        var storagePath = _configuration["StoragePath"] ?? "Data/Snapshots";
        var storageRoot = Path.IsPathRooted(storagePath)
            ? storagePath
            : Path.Combine(_environment.ContentRootPath, storagePath);
        var safeWorkloadId = Path.GetFileName(workloadId);
        var workloadDir = Path.Combine(storageRoot, safeWorkloadId);

        if (!Directory.Exists(workloadDir))
        {
            return null;
        }

        var latestFile = new DirectoryInfo(workloadDir)
            .GetFiles("*.tar.gz")
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .FirstOrDefault();

        return latestFile?.FullName;
    }

    private string BuildDownloadUrl(string workloadId)
    {
        var baseUrl = _configuration["ArtifactBaseUrl"] ?? "http://localhost:8080";
        var safeWorkloadId = Path.GetFileName(workloadId);
        return $"{baseUrl.TrimEnd('/')}/download/{safeWorkloadId}";
    }

    private enum CommandOutcome
    {
        Completed,
        Failed,
        Timeout
    }
}
