using AetherGuard.Core.Data;
using AetherGuard.Core.Models;
using AetherGuard.Grpc.V1;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

namespace AetherGuard.Core.Services;

public class ControlPlaneService
{
    private readonly ApplicationDbContext _context;
    private readonly CommandService _commandService;
    private readonly TelemetryStore _telemetryStore;
    private readonly DynamicRiskPolicy _dynamicRiskPolicy;

    public ControlPlaneService(
        ApplicationDbContext context,
        CommandService commandService,
        TelemetryStore telemetryStore,
        DynamicRiskPolicy dynamicRiskPolicy)
    {
        _context = context;
        _commandService = commandService;
        _telemetryStore = telemetryStore;
        _dynamicRiskPolicy = dynamicRiskPolicy;
    }

    public async Task<ApiResult<QueueCommandResponse>> QueueCommandAsync(
        QueueCommandRequest request,
        CancellationToken cancellationToken)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.WorkloadId))
        {
            return ApiResult<QueueCommandResponse>.Fail(
                StatusCodes.Status400BadRequest,
                "WorkloadId is required.");
        }

        if (string.IsNullOrWhiteSpace(request.Action))
        {
            return ApiResult<QueueCommandResponse>.Fail(
                StatusCodes.Status400BadRequest,
                "Action is required.");
        }

        object parameters = request.Params is null || request.Params.Fields.Count == 0
            ? new object()
            : GrpcParameterConverter.FormatStruct(request.Params);

        var command = await _commandService.QueueCommand(
            request.WorkloadId.Trim(),
            request.Action.Trim(),
            parameters,
            cancellationToken);

        var response = new QueueCommandResponse
        {
            Status = "queued",
            CommandId = command.CommandId.ToString(),
            Nonce = command.Nonce,
            Signature = command.Signature,
            ExpiresAt = command.ExpiresAt.ToString("O")
        };

        return ApiResult<QueueCommandResponse>.Ok(response, StatusCodes.Status202Accepted);
    }

    public async Task<ApiResult<DashboardLatestResponse>> GetLatestAsync(CancellationToken cancellationToken)
    {
        var latest = _telemetryStore.GetLatest();
        if (latest is null)
        {
            return ApiResult<DashboardLatestResponse>.Fail(
                StatusCodes.Status404NotFound,
                "Not found.");
        }

        var telemetry = new DashboardTelemetry
        {
            AgentId = latest.Telemetry.AgentId,
            WorkloadTier = latest.Telemetry.WorkloadTier,
            RebalanceSignal = latest.Telemetry.RebalanceSignal,
            DiskAvailable = latest.Telemetry.DiskAvailable,
            Timestamp = latest.Telemetry.Timestamp
        };

        DashboardAnalysis? analysis = null;
        if (latest.Analysis is not null)
        {
            var semantic = await GetLatestSemanticSnapshotAsync(cancellationToken);
            var preemptProbability = ResolvePreemptProbability(latest.Analysis, latest.Telemetry.RebalanceSignal);
            var riskInput = new DynamicRiskInput(
                PreemptProbability: preemptProbability,
                RebalanceSignal: latest.Telemetry.RebalanceSignal,
                VolatilityProbability: semantic.VolatilityProbability,
                SentimentNegative: semantic.SentimentNegative,
                SentimentPositive: semantic.SentimentPositive);
            var decision = _dynamicRiskPolicy.Evaluate(
                riskInput,
                new RiskGuardrailState(
                    CooldownActive: false,
                    MaxRateExceeded: false,
                    RecentMigrationsLastHour: 0,
                    MaxMigrationsPerHour: _dynamicRiskPolicy.Options.MaxMigrationsPerHour));

            analysis = new DashboardAnalysis
            {
                Status = latest.Analysis.Status,
                Confidence = latest.Analysis.Confidence,
                PredictedCpu = ClampPrediction(latest.Analysis.Prediction),
                RootCause = latest.Analysis.RootCause ?? string.Empty,
                Alpha = decision.Alpha,
                PreemptProbability = preemptProbability,
                DecisionScore = decision.DecisionScore,
                Rationale = BuildDecisionRationale(decision, latest, semantic)
            };
            analysis.TopSignals.AddRange(BuildTopSignals(latest, latest.Analysis, preemptProbability, semantic));
        }

        var response = new DashboardLatestResponse
        {
            Telemetry = telemetry
        };

        if (analysis is not null)
        {
            response.Analysis = analysis;
        }

        return ApiResult<DashboardLatestResponse>.Ok(response);
    }

    public async Task<ApiResult<DashboardHistoryResponse>> GetHistoryAsync(
        GetDashboardHistoryRequest request,
        CancellationToken cancellationToken)
    {
        var limit = request?.Limit > 0 ? request.Limit : 20;

        var records = await _context.TelemetryRecords
            .OrderByDescending(x => x.Timestamp)
            .Take(limit)
            .Select(record => new TelemetryHistoryEntry
            {
                Id = record.Id,
                AgentId = record.AgentId,
                WorkloadTier = record.WorkloadTier,
                RebalanceSignal = record.RebalanceSignal,
                DiskAvailable = record.DiskAvailable,
                AiStatus = record.AiStatus,
                AiConfidence = record.AiConfidence,
                PredictedCpu = ClampPrediction(record.PredictedCpu),
                RootCause = record.RootCause ?? string.Empty,
                Timestamp = record.Timestamp.ToString("O")
            })
            .ToListAsync(cancellationToken);

        records.Reverse();

        var response = new DashboardHistoryResponse();
        response.Records.AddRange(records);

        return ApiResult<DashboardHistoryResponse>.Ok(response);
    }

    private static double ClampPrediction(double prediction)
        => Math.Clamp(prediction, 0, 100);

    private static double ClampPrediction(double? prediction)
        => prediction.HasValue ? Math.Clamp(prediction.Value, 0, 100) : 0;

    private async Task<SemanticSnapshot> GetLatestSemanticSnapshotAsync(CancellationToken cancellationToken)
    {
        var signal = await _context.ExternalSignals
            .AsNoTracking()
            .Where(item =>
                item.SentimentNegative.HasValue &&
                item.SentimentPositive.HasValue &&
                item.VolatilityProbability.HasValue)
            .OrderByDescending(item => item.EnrichedAt ?? item.PublishedAt)
            .Select(item => new
            {
                item.Source,
                item.Title,
                item.SummaryDigest,
                item.Summary,
                item.SentimentNegative,
                item.SentimentPositive,
                item.VolatilityProbability
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (signal is null)
        {
            return new SemanticSnapshot(
                VolatilityProbability: 0.5,
                SentimentNegative: 0.33,
                SentimentPositive: 0.33,
                Source: "fallback",
                Detail: "No enriched external signal available.");
        }

        return new SemanticSnapshot(
            VolatilityProbability: Clamp01(signal.VolatilityProbability ?? 0.5),
            SentimentNegative: Clamp01(signal.SentimentNegative ?? 0.33),
            SentimentPositive: Clamp01(signal.SentimentPositive ?? 0.33),
            Source: signal.Source,
            Detail: FirstNonEmpty(signal.SummaryDigest, signal.Summary, signal.Title, "Latest enriched signal"));
    }

    private static IEnumerable<DashboardSignal> BuildTopSignals(
        TelemetrySnapshot latest,
        AnalysisResult analysis,
        double preemptProbability,
        SemanticSnapshot semantic)
    {
        var sentimentPressure = Math.Max(0.0, Clamp01(semantic.SentimentNegative) - Clamp01(semantic.SentimentPositive));
        var candidates = new[]
        {
            new SignalCandidate(
                Key: "rebalance_signal",
                Label: "Rebalance Signal",
                Value: latest.Telemetry.RebalanceSignal ? 1.0 : 0.0,
                Influence: latest.Telemetry.RebalanceSignal ? 1.0 : 0.0,
                Source: "telemetry",
                Detail: latest.Telemetry.RebalanceSignal
                    ? "Provider rebalance hint is active."
                    : "Provider rebalance hint is inactive."),
            new SignalCandidate(
                Key: "ai_preempt_probability",
                Label: "AI P_preempt",
                Value: preemptProbability,
                Influence: preemptProbability,
                Source: "ai",
                Detail: $"status={analysis.Status}, confidence={Clamp01(analysis.Confidence):F2}, predicted_cpu={ClampPrediction(analysis.Prediction):F0}%"),
            new SignalCandidate(
                Key: "volatility_probability",
                Label: "Volatility Probability",
                Value: semantic.VolatilityProbability,
                Influence: semantic.VolatilityProbability,
                Source: $"external:{semantic.Source}",
                Detail: semantic.Detail),
            new SignalCandidate(
                Key: "sentiment_pressure",
                Label: "Sentiment Pressure",
                Value: sentimentPressure,
                Influence: sentimentPressure,
                Source: $"external:{semantic.Source}",
                Detail: $"negative={semantic.SentimentNegative:F2}, positive={semantic.SentimentPositive:F2}")
        };

        return candidates
            .OrderByDescending(candidate => candidate.Influence)
            .ThenByDescending(candidate => candidate.Value)
            .Take(3)
            .Select(candidate => new DashboardSignal
            {
                Key = candidate.Key,
                Label = candidate.Label,
                Value = candidate.Value,
                Source = candidate.Source,
                Detail = candidate.Detail
            });
    }

    private static string BuildDecisionRationale(
        DynamicRiskDecision decision,
        TelemetrySnapshot latest,
        SemanticSnapshot semantic)
    {
        if (decision.Reason == "guardrail_cooldown_active")
        {
            return "Cooldown guardrail is active; migration is temporarily suppressed.";
        }

        if (decision.Reason == "guardrail_max_rate_exceeded")
        {
            return "Migration rate guardrail is active; migration is rate-limited.";
        }

        if (latest.Telemetry.RebalanceSignal)
        {
            return "Rebalance signal is active, so alpha is pinned to max risk posture.";
        }

        var sentimentPressure = Math.Max(0.0, semantic.SentimentNegative - semantic.SentimentPositive);
        return $"Dynamic alpha is derived from volatility={semantic.VolatilityProbability:F2} and sentiment pressure={sentimentPressure:F2}.";
    }

    private static double ResolvePreemptProbability(AnalysisResult riskResult, bool rebalanceSignal)
    {
        var confidence = Clamp01(riskResult.Confidence);
        var prediction = Clamp01(riskResult.Prediction / 100.0);
        var statusCritical = string.Equals(riskResult.Status, "CRITICAL", StringComparison.OrdinalIgnoreCase);
        var unavailable = string.Equals(riskResult.Status, "Unavailable", StringComparison.OrdinalIgnoreCase);

        var baseProbability = statusCritical
            ? Math.Max(confidence, prediction)
            : prediction;
        if (rebalanceSignal && unavailable)
        {
            baseProbability = Math.Max(baseProbability, 0.9);
        }
        if (rebalanceSignal && statusCritical)
        {
            baseProbability = Math.Max(baseProbability, 0.95);
        }

        return Clamp01(baseProbability);
    }

    private static string FirstNonEmpty(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                return candidate.Trim();
            }
        }

        return string.Empty;
    }

    private static double Clamp01(double value)
    {
        if (value < 0.0)
        {
            return 0.0;
        }

        if (value > 1.0)
        {
            return 1.0;
        }

        return value;
    }

    private sealed record SemanticSnapshot(
        double VolatilityProbability,
        double SentimentNegative,
        double SentimentPositive,
        string Source,
        string Detail);

    private sealed record SignalCandidate(
        string Key,
        string Label,
        double Value,
        double Influence,
        string Source,
        string Detail);
}
