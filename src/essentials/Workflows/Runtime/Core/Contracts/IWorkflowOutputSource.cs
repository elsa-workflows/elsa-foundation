using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Reads policy-filtered durable workflow outputs, optionally including one pending checkpoint change set.</summary>
public interface IWorkflowOutputSource
{
    ValueTask<IReadOnlyCollection<RuntimeWorkflowOutput>> ReadAsync(
        string workflowExecutionId,
        IReadOnlyCollection<RuntimeStateChange<DurableValueState>>? pendingDurableValueChanges = null,
        CancellationToken cancellationToken = default);
}
