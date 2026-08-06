using System.Text.Json.Serialization;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Replay-stable generic provenance assigned before checkpoint enrichment.</summary>
public sealed class RuntimeCheckpointProvenance : IEquatable<RuntimeCheckpointProvenance>
{
    public RuntimeCheckpointProvenance(RuntimeExecutionContextSnapshot? executionContext, long workflowCheckpointOrder)
    {
        if (workflowCheckpointOrder <= 0)
            throw new ArgumentOutOfRangeException(nameof(workflowCheckpointOrder), workflowCheckpointOrder, "Workflow checkpoint order must be positive.");

        ExecutionContext = executionContext;
        WorkflowCheckpointOrder = workflowCheckpointOrder;
    }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RuntimeExecutionContextSnapshot? ExecutionContext { get; }
    public long WorkflowCheckpointOrder { get; }

    internal static RuntimeCheckpointProvenance Terminal(long workflowCheckpointOrder) =>
        new(workflowCheckpointOrder);

    private RuntimeCheckpointProvenance(long workflowCheckpointOrder)
    {
        if (workflowCheckpointOrder <= 0)
            throw new ArgumentOutOfRangeException(nameof(workflowCheckpointOrder), workflowCheckpointOrder, "Workflow checkpoint order must be positive.");
        WorkflowCheckpointOrder = workflowCheckpointOrder;
    }

    public bool Equals(RuntimeCheckpointProvenance? other) =>
        other is not null &&
        WorkflowCheckpointOrder == other.WorkflowCheckpointOrder &&
        Equals(ExecutionContext, other.ExecutionContext);

    public override bool Equals(object? obj) => obj is RuntimeCheckpointProvenance other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(ExecutionContext, WorkflowCheckpointOrder);
}
