namespace AetherGuard.Core.Services;

public sealed class AgentInferenceOptions
{
    public bool EnableLocalInferenceRollout { get; set; }
    public int RolloutPercentage { get; set; }
}
