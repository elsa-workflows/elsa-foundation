using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Dispatch;

namespace Elsa.Workflows.Runtime.Services.Executions;

/// <summary>Read-only admission preflight. The actual start must still serialize with scope teardown.</summary>
internal sealed class WorkflowTestEntryAdmission(IEnumerable<IWorkflowTestEntryContextResolver> resolvers,
    IEnumerable<IWorkflowTestScopeStore> scopes, TimeProvider clock, RuntimeTestExecutionBoundary boundary)
{
    private readonly IWorkflowTestEntryContextResolver[] _resolvers = resolvers.ToArray();
    private readonly IWorkflowTestScopeStore[] _scopes = scopes.ToArray();

    internal async ValueTask ValidateAsync(WorkflowTestEntry entry, WorkflowExecutable executable,
        WorkflowRunKind runKind, WorkflowTestScope? scope, WorkflowExecutionAuthoritySnapshot? authority,
        string? tenantId, WorkflowExecutionPartition? partition, string? parentWorkflowExecutionId,
        string? triggerNodeId, WorkflowExecutableStartAuthority? startAuthority, int dispatchNestingDepth,
        bool hasStimulusInput, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (runKind != WorkflowRunKind.TestRun || scope is null || scope.IsExpired(clock.GetUtcNow()) ||
            !WorkflowTestScope.ContextEquals(entry.Scope, scope) || scope.TenantId != tenantId ||
            partition != scope.Partition || parentWorkflowExecutionId is not null || triggerNodeId is not null ||
            hasStimulusInput || dispatchNestingDepth != 0 ||
            startAuthority is { Kind: not WorkflowExecutableStartAuthorityKind.LiveReference } ||
            authority is null || !WorkflowExecutionAuthoritySnapshot.Matches(entry.Authority,
                authority.SystemIdentity, authority.RootInitiator, authority.Metadata) ||
            !WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(entry.Executable, executable.Identity) ||
            !executable.NodesById.ContainsKey(entry.ExecutableNodeId))
            throw Refused("test-entry-context-mismatch", "The selected test entry differs from its exact executable, scope or authority.");
        if (_resolvers.Length != 1 || _scopes.Length != 1)
            throw Refused("test-entry-unavailable", "Selected test entry requires one trusted context resolver and one normal test-scope store.");
        if (!entry.Matches(await _resolvers[0].FindAsync(entry.ContextId, cancellationToken)))
            throw Refused("test-entry-not-admitted", "The exact selected test context is not admitted or is no longer available.");
        await boundary.RequireAsync(entry, cancellationToken);
        WorkflowTestScopeAdmission.EnsureOpen(await _scopes[0].FindAsync(scope.ScopeId, cancellationToken), scope, clock.GetUtcNow());
    }

    internal static WorkflowExecutableStartRejectedException Refused(string code, string message) =>
        new(WorkflowExecutableStartRejectionCategory.Authority, code, message);
}
