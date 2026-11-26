namespace Elsa.Workflows.Runtime.Core.Exceptions;

/// <summary>Thrown when one drain of a workflow execution needs more cycles than a drain cycle limit allows.</summary>
public sealed class DrainCycleLimitExceededException : InvalidOperationException
{
    public DrainCycleLimitExceededException(string workflowExecutionId, DrainCycleLimit limit, int maxCycles, int cycles)
        : base(CreateMessage(workflowExecutionId, limit, maxCycles, cycles))
    {
        WorkflowExecutionId = workflowExecutionId;
        Limit = limit;
        MaxCycles = maxCycles;
        Cycles = cycles;
    }

    public string WorkflowExecutionId { get; }

    /// <summary>Which limit the drain exceeded.</summary>
    public DrainCycleLimit Limit { get; }

    /// <summary>The value of the limit the drain exceeded.</summary>
    public int MaxCycles { get; }

    /// <summary>How many cycles the drain ran in total.</summary>
    public int Cycles { get; }

    private static string CreateMessage(string workflowExecutionId, DrainCycleLimit limit, int maxCycles, int cycles) => limit switch
    {
        DrainCycleLimit.CyclesWithoutProgress =>
            $"Workflow execution drain ran {maxCycles} cycles without progress, the maximum, for workflow execution '{workflowExecutionId}': " +
            $"none of them dispatched a work item the drain had not dispatched before. The drain ran {cycles} cycles in total.",
        DrainCycleLimit.TotalCycles =>
            $"Workflow execution drain exceeded the total cycle limit of {maxCycles} for workflow execution '{workflowExecutionId}'.",
        _ => throw new ArgumentOutOfRangeException(nameof(limit), limit, "Unknown drain cycle limit.")
    };
}

/// <summary>The drain cycle limits of <c>WorkflowDrainOrchestratorOptions</c>.</summary>
public enum DrainCycleLimit
{
    /// <summary><c>MaxCyclesWithoutProgress</c>: consecutive cycles that dispatched no work item new to the drain.</summary>
    CyclesWithoutProgress = 0,

    /// <summary><c>MaxTotalDrainCycles</c>: all the cycles of one drain.</summary>
    TotalCycles = 1
}
