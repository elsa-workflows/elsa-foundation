using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Dispatch;

namespace Elsa.Workflows.Runtime.Scheduling;

internal sealed class WorkflowTestTimePolicyResolver(
    IWorkflowExecutionStateStore workflows,
    IActivityExecutionStateStore activities,
    IWorkflowTestScopeStore scopes,
    IEnumerable<IRuntimeTestTimePolicySource> policySources,
    TimeProvider wall)
{
    private readonly IRuntimeTestTimePolicySource[] _sources = policySources.ToArray();

    internal async ValueTask<WorkflowTestTimePolicy?> ResolveAsync(string workflowId, string? invocationId,
        string? nodeId, CancellationToken cancellationToken)
    {
        var state = await workflows.FindAsync(workflowId, cancellationToken)
            ?? throw Refuse("test-time-execution-unavailable", "The runtime execution is unavailable.");
        if (state.TestEntry is not { } entry) return null;
        if (state.RunKind != WorkflowRunKind.TestRun || !WorkflowTestScope.ContextEquals(state.TestScope, entry.Scope) ||
            !WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(state.PinnedExecutable, entry.Executable) ||
            state.Authority is null || !WorkflowExecutionAuthoritySnapshot.Matches(entry.Authority,
                state.Authority.SystemIdentity, state.Authority.RootInitiator, state.Authority.Metadata))
            throw Refuse("test-time-context-mismatch", "The execution differs from its admitted test-time context.");
        if (state.Status.IsTerminal())
            throw Refuse("test-time-execution-terminal", "The test execution is already terminal.");
        if (string.IsNullOrWhiteSpace(invocationId))
            throw Refuse("test-time-invocation-required", "Test time requires the exact runtime activity invocation.");
        var activity = await activities.FindAsync(workflowId, invocationId, cancellationToken);
        if (activity is null || nodeId is not null && activity.Execution.ExecutableNodeId != nodeId ||
            activity.Status is ActivityExecutionStatus.Completed or ActivityExecutionStatus.Cancelled or
                ActivityExecutionStatus.Faulted or ActivityExecutionStatus.Superseded)
            throw Refuse("test-time-invocation-unavailable", "The exact active runtime activity invocation is unavailable.");
        return await ResolveEntryAsync(entry, cancellationToken);
    }

    internal async ValueTask<WorkflowTestTimePolicy> ResolveEntryAsync(WorkflowTestEntry entry, CancellationToken cancellationToken)
    {
        if (_sources.Length != 1)
            throw Refuse("test-time-policy-source-unavailable", "Test time requires exactly one trusted immutable policy source.");
        var policy = await _sources[0].FindAsync(entry.ContextId, entry.ContextFingerprint, cancellationToken);
        if (policy is null || !entry.Matches(policy.Entry))
            throw Refuse("test-time-policy-unavailable", "The exact admitted test-time policy is unavailable.");
        WorkflowTestScopeAdmission.EnsureOpen(await scopes.FindAsync(entry.Scope.ScopeId, cancellationToken),
            entry.Scope, wall.GetUtcNow());
        return policy;
    }

    internal static WorkflowTestTimeUnavailableException Refuse(string code, string message) => new(code, message);
}
