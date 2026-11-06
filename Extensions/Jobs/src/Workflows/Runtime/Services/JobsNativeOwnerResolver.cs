using Elsa.Jobs.Core;
using Elsa.Jobs.Workflows.Runtime.Contracts;
using Elsa.Jobs.Workflows.Runtime.Models;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Jobs.Workflows.Runtime.Services;

internal sealed class JobsNativeOwnerResolver(IEnumerable<IJobWorkflowAssociationSource> sources)
{
    internal async ValueTask<JobWorkflowOwner> ResolveAsync(WorkflowExecutionState parent, ActivityExecutionState activity,
        CancellationToken cancellationToken)
    {
        var scope = new JobScope(parent.Partition?.Value ?? throw JobsNativeBoundary.Invalid(), parent.TenantId);
        if (parent.ModelWorkOrigin is not { } origin)
            return new(1, scope, "elsa.native", parent.PinnedExecutable.DefinitionId, parent.PinnedExecutable.DefinitionVersionId,
                parent.WorkflowExecutionId, activity.InvocationId, parent.PinnedExecutable.ArtifactId, parent.PinnedExecutable.ArtifactHash, null, null);
        if (origin.WorkerExecutionId != parent.WorkflowExecutionId || origin.Scope.Value != scope.PersistencePartition ||
            !WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(origin.WorkerExecutable, parent.PinnedExecutable)) throw JobsNativeBoundary.Invalid();
        var owner = await Select(origin).ResolveAsync(origin, cancellationToken);
        if (owner is null || owner.Scope != scope || owner.Origin is not { } resolved || !resolved.IsEquivalentTo(origin)) throw JobsNativeBoundary.Invalid();
        return owner with { ModelAddress = owner.ModelAddress?.Clone() };
    }

    internal IJobWorkflowAssociationSource Select(RuntimeModelNativeWorkOrigin origin)
    {
        var matches = sources.Where(source => source.Supports(origin)).Take(2).ToArray();
        if (matches.Length == 0)
            throw new RuntimeSchedulerWorkUnavailableException("jobs.owner.unavailable", "The owning model adapter is not available for this Jobs invocation.");
        return matches.Length == 1 ? matches[0] : throw new JobException("jobs.owner.ambiguous",
            "More than one Jobs adapter claims the same model-work origin.");
    }
}
