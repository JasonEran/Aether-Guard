using AetherGuard.Core.Services;
using Xunit;

namespace AetherGuard.Core.Tests;

public class DynamicRiskPolicyTests
{
    [Fact]
    public void ComputeAlpha_ClampsToMax()
    {
        var options = new DynamicRiskOptions
        {
            BaseAlpha = 1.4,
            VolatilityWeight = 0.8,
            SentimentWeight = 0.6,
            MinAlpha = 0.5,
            MaxAlpha = 1.6
        };

        var alpha = DynamicRiskPolicy.ComputeAlpha(
            options,
            new DynamicRiskInput(
                PreemptProbability: 0.6,
                RebalanceSignal: false,
                VolatilityProbability: 1.0,
                SentimentNegative: 1.0,
                SentimentPositive: 0.0));

        Assert.Equal(1.6, alpha, 3);
    }

    [Fact]
    public void Evaluate_ReturnsCooldownBlock_WhenCooldownGuardrailActive()
    {
        var policy = new DynamicRiskPolicy(new DynamicRiskOptions());
        var decision = policy.Evaluate(
            new DynamicRiskInput(
                PreemptProbability: 0.9,
                RebalanceSignal: false,
                VolatilityProbability: 0.8,
                SentimentNegative: 0.9,
                SentimentPositive: 0.1),
            new RiskGuardrailState(
                CooldownActive: true,
                MaxRateExceeded: false,
                RecentMigrationsLastHour: 1,
                MaxMigrationsPerHour: 30));

        Assert.False(decision.ShouldMigrate);
        Assert.Equal("guardrail_cooldown_active", decision.Reason);
    }

    [Fact]
    public void Evaluate_ReturnsMaxRateBlock_WhenRateGuardrailExceeded()
    {
        var policy = new DynamicRiskPolicy(new DynamicRiskOptions());
        var decision = policy.Evaluate(
            new DynamicRiskInput(
                PreemptProbability: 0.9,
                RebalanceSignal: false,
                VolatilityProbability: 0.8,
                SentimentNegative: 0.9,
                SentimentPositive: 0.1),
            new RiskGuardrailState(
                CooldownActive: false,
                MaxRateExceeded: true,
                RecentMigrationsLastHour: 31,
                MaxMigrationsPerHour: 30));

        Assert.False(decision.ShouldMigrate);
        Assert.Equal("guardrail_max_rate_exceeded", decision.Reason);
    }

    [Fact]
    public void Evaluate_ReturnsMigrate_WhenScoreExceedsThreshold()
    {
        var policy = new DynamicRiskPolicy(new DynamicRiskOptions
        {
            BaseAlpha = 1.0,
            VolatilityWeight = 0.4,
            SentimentWeight = 0.3,
            MinAlpha = 0.5,
            MaxAlpha = 1.6,
            DecisionThreshold = 0.65
        });

        var decision = policy.Evaluate(
            new DynamicRiskInput(
                PreemptProbability: 0.7,
                RebalanceSignal: false,
                VolatilityProbability: 0.9,
                SentimentNegative: 0.8,
                SentimentPositive: 0.2),
            new RiskGuardrailState(
                CooldownActive: false,
                MaxRateExceeded: false,
                RecentMigrationsLastHour: 0,
                MaxMigrationsPerHour: 30));

        Assert.True(decision.ShouldMigrate);
        Assert.Equal("decision_score_above_threshold", decision.Reason);
    }

    [Fact]
    public void Evaluate_RebalanceSignal_UsesMaxAlpha()
    {
        var policy = new DynamicRiskPolicy(new DynamicRiskOptions
        {
            MaxAlpha = 1.4,
            DecisionThreshold = 0.9
        });

        var decision = policy.Evaluate(
            new DynamicRiskInput(
                PreemptProbability: 0.2,
                RebalanceSignal: true,
                VolatilityProbability: 0.1,
                SentimentNegative: 0.1,
                SentimentPositive: 0.9),
            new RiskGuardrailState(
                CooldownActive: false,
                MaxRateExceeded: false,
                RecentMigrationsLastHour: 0,
                MaxMigrationsPerHour: 30));

        Assert.Equal(1.4, decision.Alpha, 3);
        Assert.True(decision.ShouldMigrate);
    }
}
