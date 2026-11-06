using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Dispatch;

namespace Elsa.Workflows.Runtime.Services.Executions;

internal sealed class WorkflowTestEntryAdmission(IEnumerable<IWorkflowTestEntryContextResolver> resolvers,
    IWorkflowTestScopeStore? scopes, TimeProvider clock, RuntimeTestExecutionBoundary? boundary = null)
{
    private readonly IWorkflowTestEntryContextResolver[] _resolvers = resolvers.ToArray();

    public async ValueTask ValidateAsync(WorkflowTestEntry entry, WorkflowExecutable executable,
        WorkflowRunKind runKind, WorkflowTestScope? scope, WorkflowExecutionAuthoritySnapshot? authority,
        string? tenantId, WorkflowExecutionPartition? partition, string? parentWorkflowExecutionId,
        string? triggerNodeId, WorkflowExecutableStartAuthority? startAuthority, CancellationToken cancellationToken)
    {
        if (runKind != WorkflowRunKind.TestRun || scope is null || scope.IsExpired(clock.GetUtcNow()) ||
            !WorkflowTestScope.ContextEquals(entry.Scope, scope) || scope.TenantId != tenantId ||
            partition != scope.Partition || parentWorkflowExecutionId is not null || triggerNodeId is not null ||
            startAuthority is { Kind: not WorkflowExecutableStartAuthorityKind.LiveReference } ||
            authority is null || !WorkflowExecutionAuthoritySnapshot.Matches(entry.Authority,
                authority.SystemIdentity, authority.RootInitiator, authority.Metadata) ||
            !WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(entry.Executable, executable.Identity) ||
            !executable.NodesById.ContainsKey(entry.ExecutableNodeId))
            throw Refused("test-entry-context-mismatch", "The selected test entry differs from its exact executable, scope or authority.");
        if (boundary is null)
            throw Refused("test-entry-provider-unavailable", "Selected test entry requires qualified physical persistence and routing boundaries.");
        await boundary.RequireAsync(entry, cancellationToken);
        if (_resolvers.Length != 1 || scopes is null)
            throw Refused("test-entry-unavailable", "Selected test entry requires one trusted context resolver and the normal test-scope store.");
        var admitted = await _resolvers[0].FindAsync(entry.ContextId, cancellationToken);
        if (!entry.Matches(admitted))
            throw Refused("test-entry-not-admitted", "The exact selected test context is not admitted or is no longer available.");
        WorkflowTestScopeAdmission.EnsureOpen(await scopes.FindAsync(scope.ScopeId, cancellationToken), scope, clock.GetUtcNow());
    }

    internal static WorkflowExecutableStartRejectedException Refused(string code, string message) =>
        new(WorkflowExecutableStartRejectionCategory.Authority, code, message);
}
