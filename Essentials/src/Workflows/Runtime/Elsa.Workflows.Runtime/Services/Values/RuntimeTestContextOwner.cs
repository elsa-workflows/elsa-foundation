using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Values;

/// <summary>Canonical process-private frame owner; no ancestor invocations or secondary value store.</summary>
internal sealed class RuntimeTestContextOwner(IWorkflowExecutionStateStore workflows,
    IActivityExecutionStateStore activities) : IRuntimeTestContextFrameAccess
{
    internal const string FramePrefix = RuntimeTestContextMaterializer.FramePrefix;
    internal bool Uses(IWorkflowExecutionStateStore selectedWorkflows, IActivityExecutionStateStore selectedActivities) =>
        ReferenceEquals(workflows, selectedWorkflows) && ReferenceEquals(activities, selectedActivities);

    internal static RuntimeTestContextState? RequireContext(WorkflowExecutionState workflow)
    {
        if (workflow.TestContext is not { } context)
        {
            if (workflow.TestEntry is not null) throw new InvalidOperationException("The selected lexical owner is unavailable.");
            return null;
        }
        if (workflow.TestEntry is not { } entry || context.ContextId != entry.ContextId ||
            context.ContextFingerprint != entry.ContextFingerprint || workflow.RunKind != WorkflowRunKind.TestRun ||
            !WorkflowTestScope.ContextEquals(workflow.TestScope, entry.Scope) || workflow.TenantId != entry.Scope.TenantId ||
            workflow.Partition != entry.Scope.Partition || workflow.Authority is null ||
            !WorkflowExecutionAuthoritySnapshot.Matches(entry.Authority, workflow.Authority.SystemIdentity,
                workflow.Authority.RootInitiator, workflow.Authority.Metadata) ||
            !WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(workflow.PinnedExecutable, entry.Executable) ||
            context.Results.Count != 0 || context.OwnedNodeIds.Count != 1 || context.OwnedNodeIds[0] != entry.ExecutableNodeId)
            throw new InvalidOperationException("The persisted lexical owner differs from its admitted terminal leaf.");
        var parent = workflow.RootVariableFrame?.FrameId
            ?? throw new InvalidOperationException("The selected lexical owner has no canonical root.");
        for (var index = 0; index < context.Frames.Count; index++)
        {
            var owner = context.Frames[index];
            var frame = owner.Frame;
            if (frame.ActivationId != context.ContextId ||
                frame.FrameId != $"{FramePrefix}{context.ContextId.Length}:{context.ContextId}:{index}:{frame.ScopeId}" ||
                owner.IsClosedBoundary != (frame.Kind == VariableFrameKind.Root) ||
                frame.ParentFrameId != (owner.IsClosedBoundary ? null : parent))
                throw new InvalidOperationException("The persisted lexical frame chain differs from its admitted owner.");
            parent = frame.FrameId;
        }
        return context;
    }

    internal static IReadOnlyList<VariableFrameState> EnclosingFrames(WorkflowExecutionState workflow,
        VariableFrameState root, out RuntimeTestContextFrame? closed)
    {
        var context = RequireContext(workflow);
        var frames = new List<VariableFrameState> { root };
        closed = null;
        foreach (var supplied in context?.Frames ?? [])
        {
            if (supplied.Frame.Status != VariableFrameStatus.Active)
                throw new InvalidOperationException("The selected lexical context is closed.");
            if (supplied.IsClosedBoundary) { frames.Clear(); closed = supplied; }
            frames.Add(supplied.Frame);
        }
        return frames;
    }

    public async ValueTask<RuntimeTestCaptureTarget?> FindCaptureTargetAsync(string workflowExecutionId,
        string activityExecutionId, string scopeId, CancellationToken cancellationToken = default)
    {
        var workflow = await workflows.FindAsync(workflowExecutionId, cancellationToken);
        if (workflow is null || RequireContext(workflow) is not { } context) return null;
        var activity = await activities.FindAsync(workflowExecutionId, activityExecutionId, cancellationToken)
            ?? throw new InvalidOperationException("The capture producer is unavailable.");
        if (!context.OwnedNodeIds.Contains(activity.Execution.ExecutableNodeId, StringComparer.Ordinal) ||
            activity.ParentActivityExecutionId is not null)
            throw new InvalidOperationException("The capture producer is outside the admitted terminal leaf.");
        var owner = context.Frames.LastOrDefault(x => x.IsClosedBoundary);
        if (owner is null || owner.Frame.ScopeId != scopeId) return null;
        if (!owner.Writable || owner.Frame.Status != VariableFrameStatus.Active)
            throw new InvalidOperationException("The selected capture owner is not writable.");
        return new(workflowExecutionId, context.ContextId, context.ContextFingerprint, owner.Frame.FrameId, owner.Frame.ScopeId, owner.Frame.Revision);
    }

    public WorkflowExecutionState ApplyWrites(WorkflowExecutionState workflow, IReadOnlyCollection<RuntimeTestFrameWrite> writes)
    {
        if (writes.Count == 0) return workflow;
        var context = RequireContext(workflow) ?? throw new InvalidOperationException("The lexical capture owner is unavailable.");
        foreach (var group in writes.GroupBy(x => x.Target.FrameId, StringComparer.Ordinal))
        {
            var owner = context.Frames.LastOrDefault(x => x.IsClosedBoundary)
                ?? throw new InvalidOperationException("The visible closed lexical capture owner is unavailable.");
            if (owner.Frame.FrameId != group.Key || group.Any(x => x.Target.WorkflowExecutionId != workflow.WorkflowExecutionId ||
                x.Target.ContextFingerprint != context.ContextFingerprint || x.Target.ContextId != context.ContextId ||
                x.Target.ScopeId != owner.Frame.ScopeId || x.Target.FrameRevision != owner.Frame.Revision) ||
                group.Select(x => x.VariableKey).Distinct(StringComparer.Ordinal).Count() != group.Count())
                throw new InvalidOperationException("The lexical capture frame changed or has competing writes.");
            foreach (var write in group)
            {
                var current = context.Frames.Single(x => x.Frame.FrameId == group.Key).Frame;
                context = context.Set(group.Key, write.VariableKey, write.Value, current.Revision);
            }
        }
        return workflow with { TestContext = context };
    }
}
