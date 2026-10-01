using Elsa.Activities.Runtime.Tasks;
using Elsa.Tasks.Core;
using Elsa.Tasks.Core.Attributes;
using Elsa.Workflows.Runtime.Reconciliation.Contracts;
using Microsoft.Extensions.Logging;

namespace Elsa.Workflows.Runtime.Reconciliation.Startup;

/// <summary>
/// Runs one artifact reconciliation pass at shell activation, before readiness.
/// </summary>
/// <remarks>
/// <para>
/// <b>Ordered after <see cref="RegisterActivityTypesStartupTask"/></b>, which is what makes the import gate
/// meaningful at all: one of its two axes asks whether each node's CLR activity type is present in this runtime,
/// and before the assembly scan has populated the well-known type registry the honest answer to that question is
/// "not yet" for every type. Running first would reject every artifact on a cold start.
/// </para>
/// <para>
/// <b>A <c>[SingleNodeTask]</c>: one node at a time, and every node in turn</b> (#2192). Each node's mounted set is its
/// own, so a node must never skip its pass because another node is running one: it waits for the lock and then
/// reconciles. A pass that runs after another finds the artifacts both mount already active.
/// </para>
/// <para>
/// <b>Why the passes still take turns</b> (#2274). Unlike the design-side version reconcilers, whose concurrent passes
/// converge (#2189), two passes over one mounted set activate each artifact under the same activation id. A losing call no
/// longer compensates the winner (#2251), two completions of one slot both succeed (#2265), and each call holds a
/// root-write lease of its own (#2274), but two windows of that race still fail silently: a call cancelled while its slot
/// transition is in flight hands the slot back, and a call whose mint or preparation fails or is cancelled before the
/// other's transition lands can leave the slot naming an activation that serves nothing. Either way the other node has logged the artifact
/// live. The README's "Why the passes take turns" gives the details, and a third, loud case; switching the slot and the
/// projections in one transaction (#2230) closes the two windows.
/// </para>
/// <para>
/// Re-reconciliation needs no new trigger: this is an <see cref="IStartupTask"/>, so a shell reload replays it
/// (FR-B-008).
/// </para>
/// </remarks>
[SingleNodeTask]
[TaskDependency(typeof(RegisterActivityTypesStartupTask))]
public sealed class WorkflowArtifactReconcilerStartupTask(
    IWorkflowArtifactReconciler reconciler,
    ILogger<WorkflowArtifactReconcilerStartupTask> logger) : IStartupTask
{
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var result = await reconciler.ReconcileAsync(cancellationToken);

        // Rejections are reported, not thrown: a broken closure unit must not stop the shell from starting with
        // the units that did import. The pass result already carries a named diagnostic per rejected artifact.
        foreach (var rejection in result.Rejections)
            logger.LogWarning(
                "Workflow artifact '{ArtifactId}' from '{Origin}' was not imported ({Kind}): {Diagnostic}",
                rejection.ArtifactId,
                rejection.Origin,
                rejection.RejectionKind,
                rejection.Diagnostic);

        // T118. An ownership skip is not a rejection — the artifact imported cleanly — but it is the one non-
        // rejection outcome an operator MUST be told about at boot: the mount is being ignored for that
        // definition, and every other surface looks healthy. Left to the pass result alone it would be a silent
        // skip, which is exactly the failure the rule was approved with a diagnostic attached to avoid.
        foreach (var skip in result.OwnershipSkips)
            logger.LogWarning(
                "Workflow artifact '{ArtifactId}' from '{Origin}' was imported but NOT activated: {Diagnostic}",
                skip.ArtifactId,
                skip.Origin,
                skip.Diagnostic);
    }
}
