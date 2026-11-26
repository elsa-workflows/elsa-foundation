using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>Stores workflow-dispatch lifecycle records.</summary>
public interface IWorkflowDispatchStore
{
    ValueTask<WorkflowDispatchRecord> SaveAsync(WorkflowDispatchRecord record, CancellationToken cancellationToken = default);
    ValueTask<WorkflowDispatchRecord?> FindAsync(string dispatchId, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyCollection<WorkflowDispatchRecord>> ListAsync(string parentWorkflowExecutionId, CancellationToken cancellationToken = default);
}
