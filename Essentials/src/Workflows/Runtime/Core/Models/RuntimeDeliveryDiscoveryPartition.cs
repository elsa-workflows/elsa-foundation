using System.Text.Json.Serialization;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Exact structural discovery context captured by a trusted family/host owner, never client authority.</summary>
/// <remarks>
/// Null tenant, execution partition and test scope are exact values, not wildcards or inferred defaults.
/// Only defined run kinds are structural data; Unknown remains exact legacy classification, never production.
/// TestRun requires the complete finite test context and matching explicit
/// execution partition/tenant. PublishedRun and BackgroundWeaverRun carry no test scope. Opaque server strings
/// retain exact UTF-16 code units. Scope and execution partition are separately captured, never derived from tenant.
/// AuthorityPartitionKey is the existing coarse query projection; full ordinal authority and metadata equality
/// remains mandatory before limiting discovery or mutating a claim. Construction proves no authorization, current
/// test-ledger state, canonical header, schema capability or physical transaction. Providers reread those facts.
/// </remarks>
public sealed class RuntimeDeliveryDiscoveryPartition
{
    [JsonConstructor]
    public RuntimeDeliveryDiscoveryPartition(PersistenceScope scope, string? tenantId, WorkflowRunKind runKind,
        WorkflowExecutionAuthoritySnapshot authority, WorkflowExecutionPartition? executionPartition = null,
        WorkflowTestScope? testScope = null)
    {
        ArgumentNullException.ThrowIfNull(scope); ArgumentNullException.ThrowIfNull(authority);
        if (tenantId is not null) ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        if (!Enum.IsDefined(runKind))
            throw new ArgumentOutOfRangeException(nameof(runKind), "A defined discovery run kind is required.");
        if (runKind == WorkflowRunKind.TestRun)
        {
            if (testScope is null || executionPartition is null ||
                !StringComparer.Ordinal.Equals(testScope.TenantId, tenantId) || testScope.Partition != executionPartition)
                throw new ArgumentException("Test discovery requires the complete matching finite test context.", nameof(testScope));
        }
        else if (testScope is not null)
            throw new ArgumentException("Only TestRun discovery can carry a test scope.", nameof(testScope));
        Scope = scope; TenantId = tenantId; RunKind = runKind; Authority = authority;
        ExecutionPartition = executionPartition; TestScope = testScope;
        AuthorityPartitionKey = WorkflowExecutionAuthoritySnapshot.PartitionKey(authority.SystemIdentity,
            authority.RootInitiator, authority.Metadata);
    }

    public PersistenceScope Scope { get; }
    public string? TenantId { get; }
    public WorkflowRunKind RunKind { get; }
    public WorkflowExecutionAuthoritySnapshot Authority { get; }
    public WorkflowExecutionPartition? ExecutionPartition { get; }
    public WorkflowTestScope? TestScope { get; }
    public string AuthorityPartitionKey { get; }

    /// <summary>Compares every captured fact; equal query hashes alone never establish a match.</summary>
    /// <remarks>The other value must come from checked canonical family/host context, not a later caller's claims.</remarks>
    public bool Matches(RuntimeDeliveryDiscoveryPartition other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return Scope == other.Scope && StringComparer.Ordinal.Equals(TenantId, other.TenantId) &&
            RunKind == other.RunKind && ExecutionPartition == other.ExecutionPartition &&
            WorkflowTestScope.ContextEquals(TestScope, other.TestScope) &&
            WorkflowExecutionAuthoritySnapshot.Matches(Authority, other.Authority.SystemIdentity,
                other.Authority.RootInitiator, other.Authority.Metadata);
    }

    internal void ValidateCurrent(PersistenceAccessContext access, DateTimeOffset observedAt)
    {
        ArgumentNullException.ThrowIfNull(access);
        if (access.RequireScope() != Scope)
            throw new InvalidOperationException("Discovery does not belong to the current persistence scope.");
        if (TestScope?.IsExpired(observedAt) == true)
            throw new InvalidOperationException("The discovery test context has expired.");
    }

    public override string ToString() => nameof(RuntimeDeliveryDiscoveryPartition);
}
