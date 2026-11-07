namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Exact admitted entry into a retained full executable. This runtime descriptor is resolved from trusted test
/// context, never from authored node metadata; it does not itself authorize a start or supply lexical fixtures.
/// </summary>
public sealed class WorkflowTestEntry
{
    public WorkflowTestEntry(string contextId, string contextFingerprint, WorkflowExecutableIdentity executable,
        string executableNodeId, WorkflowTestScope scope, WorkflowExecutionAuthoritySnapshot authority)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contextId);
        ArgumentException.ThrowIfNullOrWhiteSpace(contextFingerprint);
        ArgumentNullException.ThrowIfNull(executable);
        ArgumentException.ThrowIfNullOrWhiteSpace(executableNodeId);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(authority);
        ContextId = contextId;
        ContextFingerprint = contextFingerprint;
        Executable = executable;
        ExecutableNodeId = executableNodeId;
        Scope = scope;
        Authority = authority;
    }

    public string ContextId { get; }
    public string ContextFingerprint { get; }
    public WorkflowExecutableIdentity Executable { get; }
    public string ExecutableNodeId { get; }
    public WorkflowTestScope Scope { get; }
    public WorkflowExecutionAuthoritySnapshot Authority { get; }

    /// <summary>Compares every admitted identity, including the normal authority snapshot.</summary>
    public bool Matches(WorkflowTestEntry? other) => other is not null &&
        ContextId == other.ContextId && ContextFingerprint == other.ContextFingerprint &&
        ExecutableNodeId == other.ExecutableNodeId &&
        WorkflowExecutableIdentityComparer.MatchesPinnedSnapshot(Executable, other.Executable) &&
        WorkflowTestScope.ContextEquals(Scope, other.Scope) &&
        WorkflowExecutionAuthoritySnapshot.Matches(Authority, other.Authority.SystemIdentity,
            other.Authority.RootInitiator, other.Authority.Metadata);
}
