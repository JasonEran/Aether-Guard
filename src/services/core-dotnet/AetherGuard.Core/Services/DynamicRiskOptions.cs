namespace AetherGuard.Core.Services;

public sealed class DynamicRiskOptions
{
    public double BaseAlpha { get; set; } = 0.8;
    public double VolatilityWeight { get; set; } = 0.4;
    public double SentimentWeight { get; set; } = 0.3;
    public double MinAlpha { get; set; } = 0.5;
    public double MaxAlpha { get; set; } = 1.6;
    public double DecisionThreshold { get; set; } = 0.75;
    public int CooldownMinutes { get; set; } = 2;
    public int MaxMigrationsPerHour { get; set; } = 30;
}
