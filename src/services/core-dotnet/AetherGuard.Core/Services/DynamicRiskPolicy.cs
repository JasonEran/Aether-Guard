using Microsoft.Extensions.Options;

namespace AetherGuard.Core.Services;

public sealed record DynamicRiskInput(
    double PreemptProbability,
    bool RebalanceSignal,
    double VolatilityProbability,
    double SentimentNegative,
    double SentimentPositive);

public sealed record RiskGuardrailState(
    bool CooldownActive,
    bool MaxRateExceeded,
    int RecentMigrationsLastHour,
    int MaxMigrationsPerHour);

public sealed record DynamicRiskDecision(
    bool ShouldMigrate,
    double Alpha,
    double DecisionScore,
    string Reason);

public sealed class DynamicRiskPolicy
{
    private readonly DynamicRiskOptions _options;

    public DynamicRiskPolicy(IOptions<DynamicRiskOptions> options)
        : this(options.Value)
    {
    }

    public DynamicRiskPolicy(DynamicRiskOptions options)
    {
        _options = NormalizeOptions(options);
    }

    public DynamicRiskOptions Options => _options;

    public DynamicRiskDecision Evaluate(
        DynamicRiskInput input,
        RiskGuardrailState guardrails)
    {
        if (guardrails.CooldownActive)
        {
            return new DynamicRiskDecision(
                ShouldMigrate: false,
                Alpha: 0.0,
                DecisionScore: 0.0,
                Reason: "guardrail_cooldown_active");
        }

        if (guardrails.MaxRateExceeded)
        {
            return new DynamicRiskDecision(
                ShouldMigrate: false,
                Alpha: 0.0,
                DecisionScore: 0.0,
                Reason: "guardrail_max_rate_exceeded");
        }

        var alpha = ComputeAlpha(_options, input);
        var preemptProbability = Clamp01(input.RebalanceSignal
            ? Math.Max(input.PreemptProbability, 1.0)
            : input.PreemptProbability);
        var decisionScore = Clamp01(preemptProbability * alpha);
        var shouldMigrate = decisionScore >= _options.DecisionThreshold;

        return new DynamicRiskDecision(
            ShouldMigrate: shouldMigrate,
            Alpha: alpha,
            DecisionScore: decisionScore,
            Reason: shouldMigrate ? "decision_score_above_threshold" : "decision_score_below_threshold");
    }

    public static double ComputeAlpha(DynamicRiskOptions options, DynamicRiskInput input)
    {
        var normalized = NormalizeOptions(options);
        return ComputeAlphaInternal(normalized, input);
    }

    private static double ComputeAlphaInternal(DynamicRiskOptions options, DynamicRiskInput input)
    {
        if (input.RebalanceSignal)
        {
            return options.MaxAlpha;
        }

        var volatility = Clamp01(input.VolatilityProbability);
        var sentimentPressure = Math.Max(0.0, Clamp01(input.SentimentNegative) - Clamp01(input.SentimentPositive));
        var alpha = options.BaseAlpha
                    + options.VolatilityWeight * volatility
                    + options.SentimentWeight * sentimentPressure;

        return Clamp(alpha, options.MinAlpha, options.MaxAlpha);
    }

    private static DynamicRiskOptions NormalizeOptions(DynamicRiskOptions options)
    {
        var minAlpha = options.MinAlpha <= 0 ? 0.1 : options.MinAlpha;
        var maxAlpha = options.MaxAlpha < minAlpha ? minAlpha : options.MaxAlpha;

        return new DynamicRiskOptions
        {
            BaseAlpha = Clamp(options.BaseAlpha, minAlpha, maxAlpha),
            VolatilityWeight = Math.Max(0.0, options.VolatilityWeight),
            SentimentWeight = Math.Max(0.0, options.SentimentWeight),
            MinAlpha = minAlpha,
            MaxAlpha = maxAlpha,
            DecisionThreshold = Clamp01(options.DecisionThreshold),
            CooldownMinutes = Math.Max(0, options.CooldownMinutes),
            MaxMigrationsPerHour = Math.Max(1, options.MaxMigrationsPerHour)
        };
    }

    private static double Clamp01(double value)
        => Clamp(value, 0.0, 1.0);

    private static double Clamp(double value, double min, double max)
    {
        if (value < min)
        {
            return min;
        }

        if (value > max)
        {
            return max;
        }

        return value;
    }
}
