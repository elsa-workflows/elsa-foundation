namespace Elsa.Workflows.Runtime.Core.Constants;

/// <summary>Stable runtime provenance for system-owned incident-resolution outcomes.</summary>
public static class IncidentResolutionSystemSources
{
    public const string StructuralFaultAbsorption = "StructuralFaultAbsorption";
    public const string SubtreeCancellation = "SubtreeCancellation";
    public const string ActivityActivationFailure = "ActivityActivationFailure";
    public const string PoisonedSchedulerWork = "PoisonedSchedulerWork";
    public const string MissingStrategyImplementation = "MissingStrategyImplementation";
    public const string IncidentStrategyFailure = "IncidentStrategyFailure";
}
