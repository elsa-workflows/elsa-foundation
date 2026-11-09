using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Executions;

namespace Elsa.Workflows.Runtime.Services.Alterations;

/// <summary>
/// Runs a claimed alteration job under the target run's ownership lease, as a drain runs. The alteration reads the
/// run's state and commits its checkpoint while it holds the lease, so the checkpoint carries the lease's fence and
/// the durable store refuses it once another owner has taken the run.
/// </summary>
/// <remarks>
/// On one node the actor mailbox already serializes an alteration with drains. Across nodes only placement kept them
/// apart: an alteration that reached another node wrote the run's rows beside a live drain, with nothing to stop
/// either from overwriting the other (#2667). Acquiring takes the run over, exactly as a drain's acquisition does.
/// The drain that held it is then refused at its next commit, and an alteration that loses the run to a later owner
/// is refused at its own. An alteration reached from inside a flow that already holds the run's lease runs under that
/// lease: acquiring again would fence out its own caller.
/// </remarks>
internal sealed class LeasedWorkflowAlterationActorCommandExecutor(
    IWorkflowAlterationActorCommandExecutor inner,
    IRuntimeExecutionOwnershipService ownershipService,
    IRuntimeExecutionOwnershipContextAccessor ownershipContext,
    IPersistenceOperationScopeFactory? heartbeatScopes,
    TimeProvider timeProvider) : IWorkflowAlterationActorCommandExecutor
{
    private static readonly object Completed = new();
    private readonly RuntimeExecutionLeaseRunner _leaseRunner = new(ownershipService, ownershipContext, heartbeatScopes, timeProvider);

    public async ValueTask ExecuteAsync(WorkflowExecutionCommandEnvelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        if (ownershipContext.Current is { } held &&
            StringComparer.Ordinal.Equals(held.WorkflowExecutionId, envelope.WorkflowExecutionId))
        {
            await inner.ExecuteAsync(envelope, cancellationToken);
            return;
        }

        await _leaseRunner.RunAsync(
            envelope.WorkflowExecutionId,
            envelope.Partition,
            async leasedCancellation =>
            {
                await inner.ExecuteAsync(envelope, leasedCancellation);
                return Completed;
            },
            cancellationToken);
    }
}
