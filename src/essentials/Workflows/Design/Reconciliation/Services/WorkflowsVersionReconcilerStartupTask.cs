using Elsa.Tasks.Core;
using Elsa.Tasks.Core.Attributes;
using Elsa.Workflows.Design.Core.Reconciliation;

namespace Elsa.Workflows.Design.Reconciliation.Services;

/// <summary>
/// Runs one workflow version reconciliation pass at shell start, on every node.
/// </summary>
/// <remarks>
/// It takes no lock and is not a <c>[SingleNodeTask]</c> (#2192). Its sources are node-local, such as a JSON mount or a
/// local clone, so a node that skipped the pass because another node held a lock would never reconcile its own. Two nodes
/// running it at once converge instead (#2187, #2189): version ids are derived from the source, so the second node's writes
/// replay the first one's.
/// </remarks>
[Order(2)]
public sealed class WorkflowsVersionReconcilerStartupTask(IWorkflowVersionReconciler reconciler) : IStartupTask
{
    public Task ExecuteAsync(CancellationToken cancellationToken) => reconciler.Reconcile(cancellationToken);
}
