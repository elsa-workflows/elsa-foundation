using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>Structural partition mechanics only; no discovery query, claim or physical capability is exercised.</summary>
public sealed class RuntimeDeliveryDiscoveryPartitionTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 0, 0, 0, TimeSpan.Zero);
    private static readonly PersistenceScope Scope = new("store");
    private static readonly WorkflowExecutionPartition ExecutionPartition = new("execution");

    [Fact]
    public void The_preserved_constructor_is_unspecified_and_never_means_a_wildcard()
    {
        var request = new RuntimeDeliveryClaimRequest(Scope, ["family"], ["dispatch"], "owner", TimeSpan.FromSeconds(1));
        Assert.Null(request.Partition);
        Assert.Throws<NotSupportedException>(() => request.RequirePartition(PersistenceAccessContext.Scoped(Scope), Production(), Now));
    }

    [Fact]
    public void An_explicit_request_retains_collection_snapshots_and_the_full_checked_context()
    {
        var families = new[] { "family" }; var kinds = new[] { "dispatch" };
        var partition = Production();
        var request = new RuntimeDeliveryClaimRequest(Scope, families, kinds, "owner", TimeSpan.FromSeconds(1), 2, partition);
        families[0] = kinds[0] = "changed";
        Assert.Same(partition, request.RequirePartition(PersistenceAccessContext.Scoped(Scope), Production(), Now));
        Assert.Equal("family", Assert.Single(request.Families)); Assert.Equal("dispatch", Assert.Single(request.WorkKinds));
        Assert.Throws<NotSupportedException>(() => ((IList<string>)request.WorkKinds)[0] = "changed");
    }

    [Theory]
    [InlineData("scope")]
    [InlineData("tenant")]
    [InlineData("execution-partition")]
    [InlineData("run-kind")]
    [InlineData("system")]
    [InlineData("root")]
    [InlineData("metadata-key")]
    [InlineData("metadata-value")]
    [InlineData("extra-metadata")]
    public void Every_exact_partition_dimension_refuses_a_different_trusted_context(string changed)
    {
        var original = Production(tenant: "tenant", execution: ExecutionPartition, authority: Authority());
        var metadata = changed switch
        {
            "metadata-key" => new Dictionary<string, string> { ["Key"] = "value" },
            "metadata-value" => new Dictionary<string, string> { ["key"] = "Value" },
            "extra-metadata" => new Dictionary<string, string> { ["key"] = "value", ["extra"] = "value" },
            _ => new Dictionary<string, string> { ["key"] = "value" }
        };
        var other = Production(scope: changed == "scope" ? new("other") : Scope,
            tenant: changed == "tenant" ? "Tenant" : "tenant",
            execution: changed == "execution-partition" ? new("other") : ExecutionPartition,
            kind: changed == "run-kind" ? WorkflowRunKind.BackgroundWeaverRun : WorkflowRunKind.PublishedRun,
            authority: new(changed == "system" ? "System" : "system", changed == "root" ? "Root" : "root", metadata));
        Assert.False(original.Matches(other));
        Assert.Throws<InvalidOperationException>(() => Request(original).RequirePartition(PersistenceAccessContext.Scoped(Scope), other, Now));
    }

    [Fact]
    public void Null_tenant_and_execution_partition_remain_exact_and_are_not_defaulted()
    {
        var absent = Production();
        Assert.True(absent.Matches(Production()));
        Assert.False(absent.Matches(Production(tenant: "tenant")));
        Assert.False(absent.Matches(Production(execution: new(WorkflowExecutionPartition.DefaultValue))));
        Assert.Null(Request(absent).RequirePartition(PersistenceAccessContext.Scoped(Scope), Production(), Now).ExecutionPartition);
    }

    [Fact]
    public void Authority_metadata_is_immutable_ordinal_and_order_independent()
    {
        var metadata = new Dictionary<string, string> { ["one"] = "1", ["two"] = "2" };
        var authority = new WorkflowExecutionAuthoritySnapshot("system", "root", metadata);
        var original = Production(authority: authority);
        metadata["one"] = "changed"; metadata.Clear();
        var reordered = Production(authority: new("system", "root", new Dictionary<string, string> { ["two"] = "2", ["one"] = "1" }));
        Assert.True(original.Matches(reordered));
        Assert.Equal(original.AuthorityPartitionKey, reordered.AuthorityPartitionKey);
        Assert.Equal(WorkflowExecutionAuthoritySnapshot.PartitionKey(authority.SystemIdentity, authority.RootInitiator, authority.Metadata), original.AuthorityPartitionKey);
        Assert.Throws<NotSupportedException>(() => ((IDictionary<string, string>)original.Authority.Metadata).Clear());
    }

    [Fact]
    public void An_equal_existing_authority_query_key_cannot_hide_distinct_opaque_code_units()
    {
        // Preserve the existing key recipe. Its Unicode fallback can alias these opaque server values;
        // complete ordinal context validation must remain authoritative even when a query key matches.
        var first = Production(authority: new("system-\ud800", "root", new Dictionary<string, string> { ["opaque\0key"] = "value-\udc00" }));
        var other = Production(authority: new("system-\ud801", "root", new Dictionary<string, string> { ["opaque\0key"] = "value-\udc00" }));
        Assert.Equal(first.AuthorityPartitionKey, other.AuthorityPartitionKey);
        Assert.False(first.Matches(other));
        Assert.Throws<InvalidOperationException>(() => Request(first).RequirePartition(PersistenceAccessContext.Scoped(Scope), other, Now));
        Assert.Equal("system-\ud800", first.Authority.SystemIdentity);
        Assert.Equal("value-\udc00", first.Authority.Metadata["opaque\0key"]);
    }

    [Fact]
    public void Opaque_scope_tenant_partition_and_finite_test_IDs_preserve_exact_boundaries()
    {
        var scope = new PersistenceScope("store-\ud800");
        var execution = new WorkflowExecutionPartition("part-\udc00");
        var test = new WorkflowTestScope("test\0id", Now.AddHours(1), "tenant-\ud801", execution);
        var partition = new RuntimeDeliveryDiscoveryPartition(scope, test.TenantId, WorkflowRunKind.TestRun, Authority(), execution, test);
        Assert.Equal("store-\ud800", partition.Scope.Value); Assert.Equal("tenant-\ud801", partition.TenantId);
        Assert.Equal("part-\udc00", partition.ExecutionPartition!.Value); Assert.Equal("test\0id", partition.TestScope!.ScopeId);
        Assert.True(partition.Matches(new(scope, test.TenantId, WorkflowRunKind.TestRun, Authority(), execution, test)));
        Assert.False(Production(new("a\0b"), "c").Matches(Production(new("a"), "b\0c")));
        Assert.False(Production(authority: new("ab", "c")).Matches(Production(authority: new("a", "bc"))));
    }

    [Fact]
    public void A_tenantless_finite_test_context_is_exact_and_never_a_named_tenant_wildcard()
    {
        var test = new WorkflowTestScope("test", Now.AddHours(1), null, ExecutionPartition);
        var tenantless = new RuntimeDeliveryDiscoveryPartition(Scope, null, WorkflowRunKind.TestRun, Authority(), ExecutionPartition, test);
        var same = new RuntimeDeliveryDiscoveryPartition(Scope, null, WorkflowRunKind.TestRun, Authority(), ExecutionPartition,
            new("test", Now.AddHours(1), null, ExecutionPartition));
        Assert.Same(tenantless, Request(tenantless).RequirePartition(PersistenceAccessContext.Scoped(Scope), same, Now));
        Assert.False(tenantless.Matches(TestPartition()));
    }

    [Theory]
    [InlineData("missing-test")]
    [InlineData("missing-partition")]
    [InlineData("tenant")]
    [InlineData("null-tenant")]
    [InlineData("partition")]
    public void Test_runs_require_a_complete_matching_finite_context(string changed)
    {
        var test = TestScope();
        Assert.Throws<ArgumentException>(() => new RuntimeDeliveryDiscoveryPartition(Scope,
            changed == "null-tenant" ? null : changed == "tenant" ? "other" : test.TenantId,
            WorkflowRunKind.TestRun, Authority(), changed == "missing-partition" ? null : changed == "partition" ? new("other") : ExecutionPartition,
            changed == "missing-test" ? null : test));
    }

    [Theory]
    [InlineData(WorkflowRunKind.PublishedRun)]
    [InlineData(WorkflowRunKind.BackgroundWeaverRun)]
    public void Production_run_kinds_cannot_carry_test_scope(WorkflowRunKind kind) =>
        Assert.Throws<ArgumentException>(() => new RuntimeDeliveryDiscoveryPartition(Scope, "tenant", kind, Authority(), ExecutionPartition, TestScope()));

    [Fact]
    public void Explicit_Unknown_run_kind_preserves_legacy_classification_without_production_inference()
    {
        var legacy = Production(kind: WorkflowRunKind.Unknown);
        Assert.True(legacy.Matches(Production(kind: WorkflowRunKind.Unknown)));
        Assert.False(legacy.Matches(Production()));
        Assert.Throws<InvalidOperationException>(() => Request(legacy).RequirePartition(PersistenceAccessContext.Scoped(Scope), Production(), Now));
    }

    [Theory]
    [InlineData((WorkflowRunKind)99)]
    public void Undefined_run_classification_refuses(WorkflowRunKind kind) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Production(kind: kind));

    [Theory]
    [InlineData("id")]
    [InlineData("expiry")]
    public void Test_identity_and_expiry_both_participate_in_exact_context(string changed)
    {
        var first = TestPartition();
        var otherTest = new WorkflowTestScope(changed == "id" ? "other" : "test", changed == "expiry" ? Now.AddHours(2) : Now.AddHours(1), "tenant", ExecutionPartition);
        var other = new RuntimeDeliveryDiscoveryPartition(Scope, "tenant", WorkflowRunKind.TestRun, Authority(), ExecutionPartition, otherTest);
        Assert.False(first.Matches(other));
        Assert.Throws<InvalidOperationException>(() => Request(first).RequirePartition(PersistenceAccessContext.Scoped(Scope), other, Now));
    }

    [Fact]
    public void Equivalent_expiry_instants_match_but_discovery_stops_at_the_expiry_boundary()
    {
        var first = TestPartition();
        var equivalent = new RuntimeDeliveryDiscoveryPartition(Scope, "tenant", WorkflowRunKind.TestRun, Authority(), ExecutionPartition,
            new("test", Now.AddHours(1).ToOffset(TimeSpan.FromHours(2)), "tenant", ExecutionPartition));
        Assert.True(first.Matches(equivalent));
        var request = Request(first); var access = PersistenceAccessContext.Scoped(Scope);
        Assert.Same(first, request.RequirePartition(access, equivalent, Now.AddHours(1).AddTicks(-1)));
        Assert.Throws<InvalidOperationException>(() => request.RequirePartition(access, equivalent, Now.AddHours(1)));
        Assert.Throws<InvalidOperationException>(() => request.RequirePartition(access, equivalent, Now.AddHours(1).AddTicks(1)));
    }

    [Theory]
    [InlineData("other")]
    [InlineData("global")]
    [InlineData("across")]
    [InlineData("privileged")]
    public void Ordinary_same_scope_access_is_required_even_for_matching_partition_values(string accessKind)
    {
        var purpose = new PersistenceAccessPurpose("maintenance");
        var access = accessKind switch
        {
            "global" => PersistenceAccessContext.Global,
            "across" => PersistenceAccessContext.PrivilegedAcrossScopes(purpose),
            "privileged" => PersistenceAccessContext.PrivilegedScoped(Scope, purpose),
            _ => PersistenceAccessContext.Scoped(new("other"))
        };
        Assert.Throws<InvalidOperationException>(() => Request(Production()).RequirePartition(access, Production(), Now));
    }

    [Fact]
    public void Claim_scope_mismatch_null_components_and_blank_optional_tenant_refuse()
    {
        Assert.Throws<ArgumentException>(() => new RuntimeDeliveryClaimRequest(new("other"), ["family"], ["dispatch"], "owner", TimeSpan.FromSeconds(1), 1, Production()));
        Assert.Throws<ArgumentNullException>(() => new RuntimeDeliveryClaimRequest(Scope, ["family"], ["dispatch"], "owner", TimeSpan.FromSeconds(1), 1, null!));
        Assert.Throws<ArgumentNullException>(() => new RuntimeDeliveryDiscoveryPartition(null!, null, WorkflowRunKind.PublishedRun, Authority()));
        Assert.Throws<ArgumentNullException>(() => new RuntimeDeliveryDiscoveryPartition(Scope, null, WorkflowRunKind.PublishedRun, null!));
        Assert.Throws<ArgumentException>(() => Production(tenant: " \t"));
        Assert.Throws<ArgumentNullException>(() => Production().Matches(null!));
        Assert.Throws<ArgumentNullException>(() => Request(Production()).RequirePartition(null!, Production(), Now));
        Assert.Throws<ArgumentNullException>(() => Request(Production()).RequirePartition(PersistenceAccessContext.Scoped(Scope), null!, Now));
    }

    [Fact]
    public void Standalone_C2_authority_reconstruction_uses_retained_requested_by_not_a_later_worker_or_business_metadata()
    {
        const string retainedRequestedBy = "event-producer";
        var captured = Production(authority: WorkflowExecutionAuthoritySnapshot.CreateRoot(retainedRequestedBy));
        var canonical = Production(authority: WorkflowExecutionAuthoritySnapshot.CreateRoot(retainedRequestedBy));
        Assert.Same(captured, Request(captured).RequirePartition(PersistenceAccessContext.Scoped(Scope), canonical, Now));
        Assert.False(captured.Matches(Production(authority: WorkflowExecutionAuthoritySnapshot.CreateRoot("later-worker"))));
        Assert.False(captured.Matches(Production(authority: WorkflowExecutionAuthoritySnapshot.CreateRoot(retainedRequestedBy,
            new Dictionary<string, string> { ["requestedBy"] = "business-value" }))));
    }

    [Fact]
    public void Ordinary_formatting_does_not_disclose_partition_authority_or_test_data()
    {
        var partition = new RuntimeDeliveryDiscoveryPartition(new("private-store"), "private-tenant", WorkflowRunKind.TestRun,
            new("private-system", "private-root", new Dictionary<string, string> { ["private-key"] = "private-value" }), new("private-partition"),
            new("private-test", Now.AddHours(1), "private-tenant", new("private-partition")));
        Assert.DoesNotContain("private", $"{partition} {Request(partition)}");
    }

    private static RuntimeDeliveryDiscoveryPartition Production(PersistenceScope? scope = null, string? tenant = null,
        WorkflowExecutionPartition? execution = null, WorkflowRunKind kind = WorkflowRunKind.PublishedRun,
        WorkflowExecutionAuthoritySnapshot? authority = null) => new(scope ?? Scope, tenant, kind, authority ?? Authority(), execution);
    private static WorkflowExecutionAuthoritySnapshot Authority() => new("system", "root", new Dictionary<string, string> { ["key"] = "value" });
    private static WorkflowTestScope TestScope() => new("test", Now.AddHours(1), "tenant", ExecutionPartition);
    private static RuntimeDeliveryDiscoveryPartition TestPartition() => new(Scope, "tenant", WorkflowRunKind.TestRun, Authority(), ExecutionPartition, TestScope());
    private static RuntimeDeliveryClaimRequest Request(RuntimeDeliveryDiscoveryPartition partition) =>
        new(partition.Scope, ["family"], ["dispatch"], "owner", TimeSpan.FromSeconds(1), 10, partition);
}
