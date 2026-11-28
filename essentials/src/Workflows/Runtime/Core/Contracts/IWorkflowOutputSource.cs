using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Reads policy-filtered durable workflow outputs, optionally including one pending checkpoint change set.</summary>
public interface IWorkflowOutputSource
{
    ValueTask<IReadOnlyCollection<RuntimeWorkflowOutput>> ReadAsync(
        string workflowExecutionId,
        IReadOnlyCollection<RuntimeStateChange<DurableValueState>>? pendingDurableValueChanges = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Reads the committed workflow output values themselves, latest per name, with the same selection and pending
    /// changes as <see cref="ReadAsync"/> but without the diagnostics capture policy. Outputs whose value protection
    /// policy is protected (sensitive, encryption, redaction mode, retention) or that are not stored inline are left
    /// out and named in <see cref="WorkflowOutputValues.WithheldNames"/>.
    /// </summary>
    ValueTask<WorkflowOutputValues> ReadValuesAsync(
        string workflowExecutionId,
        IReadOnlyCollection<RuntimeStateChange<DurableValueState>>? pendingDurableValueChanges = null,
        CancellationToken cancellationToken = default);
}
