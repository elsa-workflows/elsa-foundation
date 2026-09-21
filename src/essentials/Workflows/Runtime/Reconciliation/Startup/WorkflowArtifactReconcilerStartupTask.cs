using Elsa.Activities.Runtime.Tasks;
using Elsa.Locking.Core;
using Elsa.Tasks.Core;
using Elsa.Tasks.Core.Attributes;
using Elsa.Workflows.Runtime.Reconciliation.Contracts;
using Elsa.Workflows.Runtime.Reconciliation.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
/// <c>[SingleNodeTask]</c> plus a distributed lock, mirroring the design-side version reconciler: several nodes
/// booting against one mounted set would otherwise contend for the same activation slots, and every loser would
/// take a CAS conflict for work that was already done. A node that cannot take the lock logs and returns — that
/// is the expected outcome on all but one node, not a failure.
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
    IDistributedLockProvider distributedLockProvider,
    IOptions<WorkflowArtifactReconcilerStartupTaskOptions> options,
    ILogger<WorkflowArtifactReconcilerStartupTask> logger) : IStartupTask
{
    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var lockKey = nameof(WorkflowArtifactReconcilerStartupTask);
        var timeout = TimeSpan.FromMilliseconds(options.Value.LockTimeoutMs);
        await using var @lock = await distributedLockProvider.TryAcquireLockAsync(lockKey, timeout, cancellationToken);

        if (@lock is null)
        {
            if (logger.IsEnabled(LogLevel.Information))
                logger.LogInformation("Could not retrieve lock '{Key}'; because it was claimed by another instance", lockKey);

            return;
        }

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
