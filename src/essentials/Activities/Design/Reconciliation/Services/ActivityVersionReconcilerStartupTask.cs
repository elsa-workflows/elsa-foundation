using Elsa.Activities.Design.Core.Reconciliation;
using Elsa.Tasks.Core;
using Elsa.Tasks.Core.Attributes;

namespace Elsa.Activities.Design.Reconciliation.Services;

/// <summary>
/// Runs one activity version reconciliation pass at shell start, on every node.
/// </summary>
/// <remarks>
/// It takes no lock and is not a <c>[SingleNodeTask]</c> (#2192). Its inputs are node-local, the assemblies and catalogs
/// this node loaded, so a node that skipped the pass because another node held a lock would never reconcile its own. Two
/// nodes running it at once converge instead (#2189): the second one's writes replay or read back the first one's.
/// </remarks>
[Order(1)]
public sealed class ActivityVersionReconcilerStartupTask(IActivityVersionReconciler reconciler) : IStartupTask
{
    public Task ExecuteAsync(CancellationToken cancellationToken) => reconciler.Reconcile(cancellationToken);
}
