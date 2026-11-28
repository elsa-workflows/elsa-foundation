using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Api.Tests.Support;

internal sealed class UnobservedTestRunStore : IWorkflowExecutionStateStore
{
    public ValueTask<WorkflowExecutionState?> FindAsync(string workflowExecutionId,
        CancellationToken cancellationToken = default) => ValueTask.FromResult<WorkflowExecutionState?>(null);

    public ValueTask<WorkflowExecutionState> SaveAsync(WorkflowExecutionState state,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public ValueTask<IReadOnlyCollection<WorkflowExecutionState>> ListAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public ValueTask<WorkflowExecutionStatePage> QueryPageAsync(WorkflowExecutionStatePageQuery query,
        CancellationToken cancellationToken = default) => throw new NotSupportedException();

    public ValueTask<IReadOnlyCollection<string>> ListPinnedExecutableArtifactIdsAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public ValueTask<bool> DeleteAsync(string workflowExecutionId, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}
