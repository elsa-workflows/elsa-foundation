namespace Elsa.Workflows.Runtime.Core.Constants;

/// <summary>Persisted failure types used to classify runtime incidents across execution and inspection.</summary>
public static class RuntimeIncidentFailureTypes
{
    /// <summary>A scheduler consumer fault that may include sensitive handler exception text.</summary>
    public const string SchedulerWorkPoisoned = "SchedulerWorkPoisoned";
}
