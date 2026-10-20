namespace Elsa.Workflows.Runtime.Core.Constants;

/// <summary>Stable runtime provenance for system-owned incident-resolution outcomes.</summary>
public static class IncidentResolutionSystemSources
{
    public const string StructuralFaultAbsorption = "StructuralFaultAbsorption";
    /// <summary>A structural parent kept a child fault's incident blocking until an operator intervenes.</summary>
    public const string StructuralIntervention = "StructuralIntervention";
    public const string SubtreeCancellation = "SubtreeCancellation";
    public const string ActivityActivationFailure = "ActivityActivationFailure";
    public const string PoisonedSchedulerWork = "PoisonedSchedulerWork";
    public const string MissingStrategyImplementation = "MissingStrategyImplementation";
    public const string IncidentStrategyFailure = "IncidentStrategyFailure";
    public const string CheckpointRuleViolation = "CheckpointRuleViolation";
}
