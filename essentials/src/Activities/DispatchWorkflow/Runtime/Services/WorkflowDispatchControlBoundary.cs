using System.Globalization;
using Elsa.Activities.DispatchWorkflow.Runtime.Constants;
using Elsa.Workflows.Runtime.Core.Attributes;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.DispatchWorkflow.Runtime.Services;

/// <summary>Resolves an opted-in called root through retained dispatch and exact committed caller/root identities.</summary>
internal sealed class WorkflowDispatchControlBoundary(
    IWorkflowExecutionStateStore workflows,
    IWorkflowDispatchStore dispatches,
    IActivityExecutionStateStore activities,
    IWorkflowExecutableStore executables) : IRuntimeWorkflowControlBoundary
{
    public async ValueTask<RuntimeChildControlTarget?> ResolveAsync(string workflowExecutionId, string rootActivityExecutionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootActivityExecutionId);
        try { return await ResolveCoreAsync(workflowExecutionId, rootActivityExecutionId, cancellationToken); }
        catch (OperationCanceledException) { throw; }
        catch (WorkflowControlBoundaryException) { throw; }
        catch (Exception exception) { throw WorkflowDispatchControlPolicy.Wrap(workflowExecutionId, rootActivityExecutionId, exception); }
    }

    private async ValueTask<RuntimeChildControlTarget?> ResolveCoreAsync(string workflowExecutionId, string rootActivityExecutionId,
        CancellationToken cancellationToken)
    {
        var childWorkflow = await workflows.FindAsync(workflowExecutionId, cancellationToken)
            ?? throw WorkflowDispatchControlPolicy.Failure("missing child workflow");
        if (!childWorkflow.SystemMetadata.TryGetValue(RuntimeMetadataKeys.ChildControlDispatchId, out var dispatchId))
            return null;
        if (string.IsNullOrWhiteSpace(dispatchId))
            throw WorkflowDispatchControlPolicy.Failure("empty dispatch lookup hint");
        var dispatch = await dispatches.FindAsync(dispatchId, cancellationToken)
            ?? throw WorkflowDispatchControlPolicy.Failure("missing dispatch");
        var capability = WorkflowDispatchControlPolicy.ReadCapability(dispatch.Metadata)
            ?? throw WorkflowDispatchControlPolicy.Failure("dispatch has no control capability");
        if (dispatch.Mode != WorkflowDispatchMode.WaitForCompletion ||
            dispatch.Status is not (WorkflowDispatchStatus.Started or WorkflowDispatchStatus.Completed or WorkflowDispatchStatus.Faulted or WorkflowDispatchStatus.Cancelled) ||
            WorkflowDispatchLifecycle.WasCancelledBeforeAdmission(dispatch) ||
            dispatch.ChildWorkflowExecutionId != workflowExecutionId ||
            dispatch.ParentWorkflowExecutionId != childWorkflow.ParentWorkflowExecutionId ||
            !WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(dispatch.ChildExecutable, childWorkflow.PinnedExecutable))
            throw WorkflowDispatchControlPolicy.Failure("child does not match admitted waited dispatch");
        if (childWorkflow.TenantId != dispatch.TenantId || childWorkflow.Partition != dispatch.Partition ||
            childWorkflow.RunKind != dispatch.RunKind || childWorkflow.DispatchNestingDepth != dispatch.DispatchNestingDepth ||
            !WorkflowTestScope.ContextEquals(childWorkflow.TestScope, dispatch.TestScope) ||
            childWorkflow.Authority is not { } childAuthority ||
            !WorkflowExecutionAuthoritySnapshot.Matches(childAuthority, dispatch.Authority.SystemIdentity,
                dispatch.Authority.RootInitiator, dispatch.Authority.Metadata))
            throw WorkflowDispatchControlPolicy.Failure("child dispatch context differs");

        var parentWorkflow = await workflows.FindAsync(dispatch.ParentWorkflowExecutionId, cancellationToken)
            ?? throw WorkflowDispatchControlPolicy.Failure("missing caller workflow");
        if (parentWorkflow.TenantId != dispatch.TenantId || parentWorkflow.Partition != dispatch.Partition ||
            parentWorkflow.RunKind != dispatch.RunKind ||
            !WorkflowTestScope.ContextEquals(parentWorkflow.TestScope, dispatch.TestScope) ||
            parentWorkflow.Authority is not { } parentAuthority ||
            !WorkflowExecutionAuthoritySnapshot.Matches(dispatch.Authority, parentWorkflow.WorkflowExecutionId,
                parentAuthority.RootInitiator, parentAuthority.Metadata) ||
            parentWorkflow.DispatchNestingDepth == int.MaxValue ||
            parentWorkflow.DispatchNestingDepth + 1 != dispatch.DispatchNestingDepth)
            throw WorkflowDispatchControlPolicy.Failure("caller dispatch context differs");
        var parentExecutable = await executables.FindAsync(parentWorkflow.PinnedExecutable.ArtifactId, cancellationToken)
            ?? throw WorkflowDispatchControlPolicy.Failure("missing caller executable");
        if (!WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(parentExecutable.Identity, parentWorkflow.PinnedExecutable))
            throw WorkflowDispatchControlPolicy.Failure("caller executable differs");
        var call = await activities.FindAsync(dispatch.ParentWorkflowExecutionId, dispatch.ParentActivityExecutionId, cancellationToken)
            ?? throw WorkflowDispatchControlPolicy.Failure("missing call invocation");
        if (call.InvocationId != dispatch.ParentActivityExecutionId || call.Execution.WorkflowExecutionId != dispatch.ParentWorkflowExecutionId ||
            call.Execution.ActivityType != DispatchWorkflowConstants.ActivityType ||
            !parentExecutable.NodesById.TryGetValue(call.Execution.ExecutableNodeId, out var callNode) ||
            callNode.ActivityType != call.Execution.ActivityType || callNode.ActivityTypeVersion != call.Execution.ActivityTypeVersion)
            throw WorkflowDispatchControlPolicy.Failure("caller invocation differs");
        var admitted = await WorkflowDispatchControlPolicy.AdmitAsync(call, parentExecutable, activities, waitForCompletion: true, cancellationToken)
            ?? throw WorkflowDispatchControlPolicy.Failure("caller no longer has admitted control provenance");
        if (admitted.Key != capability.Key || admitted.Version != capability.Version)
            throw WorkflowDispatchControlPolicy.Failure("consumer capability differs");

        var childExecutable = await executables.FindAsync(childWorkflow.PinnedExecutable.ArtifactId, cancellationToken)
            ?? throw WorkflowDispatchControlPolicy.Failure("missing child executable");
        if (!WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(childExecutable.Identity, childWorkflow.PinnedExecutable))
            throw WorkflowDispatchControlPolicy.Failure("child executable differs");
        // Root invocations have no parent-scoped query. Stream the finite workflow pages and keep only
        // the root identity; never substitute an arbitrary same-node row for the committed caller's root.
        ActivityExecutionState? root = null;
        string? cursor = null;
        do
        {
            var page = await activities.ListPageAsync(new ActivityExecutionStatePageQuery(workflowExecutionId,
                continuationToken: cursor), cancellationToken);
            foreach (var candidate in page.Items.Where(candidate => candidate.ParentActivityExecutionId is null))
            {
                if (root is not null)
                    throw WorkflowDispatchControlPolicy.Failure("ambiguous child root history");
                root = candidate;
            }
            cursor = page.NextContinuationToken;
        } while (cursor is not null);
        if (root is null || root.InvocationId != rootActivityExecutionId || root.Execution.WorkflowExecutionId != workflowExecutionId ||
            root.Provenance.ParentActivityExecutionId is not null ||
            root.Provenance.SchedulingWorkflowExecutionId != workflowExecutionId ||
            root.SchedulingActivityExecutionId is not null || root.Provenance.SchedulingActivityExecutionId is not null ||
            root.Execution.ExecutableNodeId != childExecutable.RootActivity.ExecutableNodeId ||
            root.Execution.ActivityType != childExecutable.RootActivity.ActivityType ||
            root.Execution.ActivityTypeVersion != childExecutable.RootActivity.ActivityTypeVersion)
            throw WorkflowDispatchControlPolicy.Failure("called root invocation differs");
        return new RuntimeChildControlTarget(parentWorkflow.WorkflowExecutionId, call.InvocationId,
            call.ParentActivityExecutionId!, parentWorkflow.PinnedExecutable, root.InvocationId, capability.Key, capability.Version);
    }
}

internal static class WorkflowDispatchControlPolicy
{
    public static ActivityChildControlConsumerAttribute? ReadCapability(IReadOnlyDictionary<string, string> metadata)
    {
        var hasKey = metadata.TryGetValue(RuntimeMetadataKeys.ChildControlConsumerKey, out var key);
        var hasVersion = metadata.TryGetValue(RuntimeMetadataKeys.ChildControlConsumerVersion, out var versionText);
        if (!hasKey && !hasVersion)
            return null;
        if (!hasKey || !hasVersion || !int.TryParse(versionText, NumberStyles.None, CultureInfo.InvariantCulture, out var version) ||
            version.ToString(CultureInfo.InvariantCulture) != versionText)
            throw Failure("incomplete or malformed consumer declaration");
        try { return new ActivityChildControlConsumerAttribute(key!, version); }
        catch (ArgumentException exception) { throw new WorkflowDispatchControlConflictException("Workflow dispatch control consumer declaration is invalid.", exception); }
    }

    public static async ValueTask<ActivityChildControlConsumerAttribute?> AdmitAsync(ActivityExecutionState caller,
        WorkflowExecutable executable, IActivityExecutionStateStore activities, bool waitForCompletion, CancellationToken cancellationToken)
    {
        var requested = ReadCapability(caller.Provenance.Metadata);
        if (requested is null)
            return null;
        if (!waitForCompletion || caller.ParentActivityExecutionId is not { } parentId ||
            caller.Provenance.ParentActivityExecutionId != parentId ||
            caller.Provenance.SchedulingWorkflowExecutionId != caller.Execution.WorkflowExecutionId)
            throw Failure("control requires an exact waited direct-child invocation");
        var parent = await activities.FindAsync(caller.Execution.WorkflowExecutionId, parentId, cancellationToken)
            ?? throw Failure("missing direct consumer parent");
        if (parent.InvocationId != parentId || parent.Execution.WorkflowExecutionId != caller.Execution.WorkflowExecutionId ||
            !executable.NodesById.TryGetValue(parent.Execution.ExecutableNodeId, out var parentNode) ||
            parentNode.ActivityType != parent.Execution.ActivityType || parentNode.ActivityTypeVersion != parent.Execution.ActivityTypeVersion)
            throw Failure("direct consumer parent differs");
        var declared = ReadCapability(parentNode.Metadata);
        if (declared is null || declared.Key != requested.Key || declared.Version != requested.Version)
            throw Failure("pinned parent does not declare requested control protocol");
        return declared;
    }

    public static InvalidOperationException Failure(string detail) => new WorkflowDispatchControlConflictException($"Workflow dispatch control boundary is inconsistent: {detail}.");

    public static Exception CheckpointFailure(string workflow, string? root, Exception cause)
    {
        if (cause is RuntimeSchedulerWorkUnavailableException) return cause;
        var boundary = cause as WorkflowControlBoundaryException ?? Wrap(workflow, root, cause);
        return boundary.IsUnavailable
            ? new RuntimeSchedulerWorkUnavailableException("runtime.child-control.source-unavailable",
                "The admitted control source could not be read before persistence; its work remains owed.", boundary)
            : boundary;
    }

    public static WorkflowControlBoundaryException Wrap(string workflow, string? root, Exception cause) =>
        new(workflow, root, cause, isUnavailable: cause is not (WorkflowDispatchControlConflictException or ArgumentException or System.Text.Json.JsonException));
}

internal sealed class WorkflowDispatchControlConflictException(string message, Exception? innerException = null) : InvalidOperationException(message, innerException);
