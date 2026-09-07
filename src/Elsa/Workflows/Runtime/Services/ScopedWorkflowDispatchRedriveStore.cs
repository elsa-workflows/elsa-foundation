using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;

namespace Elsa.Workflows.Runtime.Services;

/// <summary>
/// Applies the operation scope selected by the host before delegating to the shared atomic in-memory
/// dispatch/outbox owner. Durable providers enforce the same check inside their access-bound transaction.
/// </summary>
public sealed class ScopedWorkflowDispatchRedriveStore(
    InMemoryRuntimeCheckpointCommitStore atomicStore,
    IPersistenceAccessContextAccessor accessContextAccessor) : IWorkflowDispatchRedriveStore
{
    public async ValueTask<WorkflowDispatchRedriveResult> RedriveAsync(
        WorkflowDispatchRedriveRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        return await atomicStore.RedriveAsync(request, accessContextAccessor.Current, cancellationToken);
    }
}
