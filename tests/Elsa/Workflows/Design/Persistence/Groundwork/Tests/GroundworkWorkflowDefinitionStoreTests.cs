using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Filters;
using Elsa.Workflows.Design.Persistence.Groundwork.Services;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Persistence.Groundwork.Composition;
using Groundwork.Kernel;
using Groundwork.Query.Model;
using Groundwork.Store;
using Xunit;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Tests;

public sealed class GroundworkWorkflowDefinitionStoreTests
{
    private static WorkflowDefinition[] Sample() =>
    [
        new() { Id = "a", Name = "Order Processing", Description = "Handles orders" },
        new() { Id = "b", Name = "Invoice Generator" },
        new() { Id = "c", Name = "Shipping", Description = "ORDER fulfilment" }
    ];

    private static (GroundworkWorkflowDefinitionStore Store, DesignGroundworkTestPersistence Raw) Seeded(
        params WorkflowDefinition[] definitions)
    {
        var raw = new DesignGroundworkTestPersistence();
        raw.RecordQueries = true;
        foreach (var definition in definitions)
            raw.SeedDefinition(definition);
        return (new GroundworkWorkflowDefinitionStore(raw, DesignGroundworkTestAccess.DefaultAccessContextAccessor), raw);
    }

    [Fact]
    public async Task Provider_and_corrupt_payload_reads_surface_domain_scoped_failures()
    {
        using var raw = new DesignGroundworkTestPersistence();
        var failure = new IOException("workflow-provider-read");
        var store = new GroundworkWorkflowDefinitionStore(new ThrowingSource(raw, failure), DesignGroundworkTestAccess.DefaultAccessContextAccessor);
        var provider = await Assert.ThrowsAsync<GroundworkProviderFailureException>(() => store.FindByIdAsync("missing"));
        Assert.Same(failure, provider.InnerException);
        Assert.Equal("workflow-provider-read", provider.InnerException!.Message);

        using var corrupt = new DesignGroundworkTestPersistence();
        corrupt.InsertRaw(WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind,
            new StorageValues(new Dictionary<string, object?>
            {
                [WorkflowsDesignStorageManifest.IdField] = "corrupt",
                [WorkflowsDesignStorageManifest.TenantIdField] = DesignGroundworkTestAccess.DefaultScopeValue,
                [WorkflowsDesignStorageManifest.SchemaVersionField] = WorkflowsDesignStorageManifest.SchemaVersion,
                [WorkflowsDesignStorageManifest.ContentField] = "null",
                ["createdAt"] = DateTimeOffset.UtcNow,
                ["lastModifiedAt"] = DateTimeOffset.UtcNow,
                [WorkflowsDesignStorageManifest.DefinitionIdField] = "corrupt",
                [WorkflowsDesignStorageManifest.DefinitionIdSearchKeyField] =
                    QuerySearchKeys.Encode("corrupt", QuerySearchKeyPolicy.UnicodeOrdinalIgnoreCase),
                [WorkflowsDesignStorageManifest.DefinitionIdLookupHashField] =
                    Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
                        QuerySearchKeys.Encode("corrupt", QuerySearchKeyPolicy.UnicodeOrdinalIgnoreCase)))).ToLowerInvariant()
            }));
        var corruptException = await Assert.ThrowsAsync<GroundworkCorruptPayloadException>(() =>
            new GroundworkWorkflowDefinitionStore(corrupt, DesignGroundworkTestAccess.DefaultAccessContextAccessor).FindByIdAsync("corrupt"));
        Assert.Contains("deserialized", corruptException.Message, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(corruptException.InnerException);
    }

    [Fact]
    public async Task Cancellation_and_domain_read_outcomes_are_not_mapped()
    {
        using var raw = new DesignGroundworkTestPersistence();
        var cancellation = new OperationCanceledException("workflow-read-cancelled");
        var cancelled = new GroundworkWorkflowDefinitionStore(new ThrowingSource(raw, cancellation), DesignGroundworkTestAccess.DefaultAccessContextAccessor);
        var exception = await Assert.ThrowsAsync<OperationCanceledException>(() => cancelled.FindByIdAsync("missing"));
        Assert.Same(cancellation, exception);
        var (store, owned) = Seeded();
        using (owned)
            await Assert.ThrowsAsync<EntityNotFoundException>(() => store.GetAsync("missing"));
    }

    [Fact]
    public async Task FindById_returns_match_via_point_read()
    {
        var (store, raw) = Seeded(Sample());
        using (raw) Assert.Equal("Invoice Generator", (await store.FindByIdAsync("b"))?.Name);
    }

    [Fact]
    public async Task FindById_returns_null_when_absent()
    {
        var (store, raw) = Seeded(Sample());
        using (raw) Assert.Null(await store.FindByIdAsync("missing"));
    }

    [Fact]
    public async Task Point_read_does_not_require_a_bounded_surface_but_named_queries_do()
    {
        var (store, raw) = Seeded(Sample());
        using (raw)
        {
            Assert.Equal("b", (await store.FindByIdAsync("b"))?.Id);
            Assert.Equal("b", (await store.ListAsync(new WorkflowDefinitionFilter { Id = "b" })).Single().Id);
            Assert.Equal(2, raw.Queries.Count);
            Assert.Equal(WorkflowsDesignStorageManifest.DefinitionByIdSearchIndex, raw.Queries[0].IndexName);
            AssertRoute(raw, WorkflowsDesignStorageManifest.DefinitionByIdSearchIndex,
                [WorkflowsDesignStorageManifest.DefinitionIdField]);
        }
    }

    [Fact]
    public async Task GetAsync_throws_when_absent()
    {
        var (store, raw) = Seeded(Sample());
        using (raw) await Assert.ThrowsAsync<EntityNotFoundException>(() => store.GetAsync("missing"));
    }

    [Fact]
    public async Task List_by_id_matches_single()
    {
        var (store, raw) = Seeded(Sample());
        using (raw)
        {
            Assert.Equal(["c"], (await store.ListAsync(new WorkflowDefinitionFilter { Id = "c" })).Select(x => x.Id));
                AssertRoute(raw, WorkflowsDesignStorageManifest.DefinitionByIdSearchIndex,
                [WorkflowsDesignStorageManifest.DefinitionIdField]);
        }
    }

    [Fact]
    public async Task List_by_ids_matches_set_membership()
    {
        var (store, raw) = Seeded(Sample());
        using (raw)
        {
            Assert.Equal(["a", "c"], (await store.ListAsync(new WorkflowDefinitionFilter { Ids = ["a", "c"] })).Select(x => x.Id).OrderBy(x => x));
                AssertRoute(raw, WorkflowsDesignStorageManifest.DefinitionByIdSearchIndex,
                [WorkflowsDesignStorageManifest.DefinitionIdField]);
        }
    }

    [Fact]
    public async Task List_by_name_matches_exact()
    {
        var (store, raw) = Seeded(Sample());
        using (raw)
        {
            Assert.Equal(["c"], (await store.ListAsync(new WorkflowDefinitionFilter { Name = "Shipping" })).Select(x => x.Id));
            AssertRoute(raw, WorkflowsDesignStorageManifest.DefinitionByNameIndex,
                [WorkflowsDesignStorageManifest.DefinitionNameLookupHashField,
                 WorkflowsDesignStorageManifest.DefinitionIdField]);
        }
    }

    [Fact]
    public async Task List_by_names_matches_set_membership()
    {
        var (store, raw) = Seeded(Sample());
        using (raw)
        {
            Assert.Equal(["a", "c"], (await store.ListAsync(new WorkflowDefinitionFilter { Names = ["Order Processing", "Shipping"] })).Select(x => x.Id).OrderBy(x => x));
            AssertRoute(raw, WorkflowsDesignStorageManifest.DefinitionByNameIndex,
                [WorkflowsDesignStorageManifest.DefinitionNameLookupHashField,
                 WorkflowsDesignStorageManifest.DefinitionIdField]);
        }
    }

    [Fact]
    public async Task List_by_search_term_fans_out_over_name_description_and_id()
    {
        var (store, raw) = Seeded(Sample());
        using (raw) Assert.Equal(["a", "c"], (await store.ListAsync(new WorkflowDefinitionFilter { SearchTerm = "order" })).Select(x => x.Id).OrderBy(x => x));
    }

    [Fact]
    public async Task List_by_description_matches_exact()
    {
        var (store, raw) = Seeded(Sample());
        using (raw)
        {
            Assert.Equal(["a"], (await store.ListAsync(new WorkflowDefinitionFilter { Description = "Handles orders" })).Select(x => x.Id));
            AssertRoute(raw, WorkflowsDesignStorageManifest.DefinitionByDescriptionIndex,
                [WorkflowsDesignStorageManifest.DefinitionDescriptionLookupHashField,
                 WorkflowsDesignStorageManifest.DefinitionIdField]);
        }
    }

    [Fact]
    public async Task Exact_name_filter_remains_complete_beyond_the_search_term_bound()
    {
        var definitions = Enumerable.Range(0, GroundworkDesignStorage.SearchTermProbeLimit)
            .Select(index => new WorkflowDefinition
            {
                Id = $"exact-{index:D5}",
                Name = index == GroundworkDesignStorage.SearchTermMaximumMatches ? "target" : $"name-{index:D5}"
            })
            .ToArray();
        using var raw = new DesignGroundworkTestPersistence { RecordQueries = true };
        raw.SeedDefinitions(definitions);
        var store = new GroundworkWorkflowDefinitionStore(raw, DesignGroundworkTestAccess.DefaultAccessContextAccessor);

        var result = await store.ListAsync(new WorkflowDefinitionFilter { Name = "target" });

        Assert.Equal(["exact-10000"], result.Select(definition => definition.Id));
        Assert.DoesNotContain(raw.Queries, query => query.Request.AcceptedScan is not null);
        Assert.All(raw.Queries, query => Assert.Equal(WorkflowsDesignStorageManifest.DefinitionByNameIndex, query.Options?.SelectedIndex));
        Assert.All(raw.Queries, query => Assert.Equal(
            [WorkflowsDesignStorageManifest.DefinitionNameLookupHashField,
             WorkflowsDesignStorageManifest.DefinitionIdField],
            query.Request.Order.Select(term => term.Column.Name)));
    }

    [Fact]
    public async Task List_without_filters_is_deterministically_id_ordered()
    {
        var (store, raw) = Seeded(Sample().Reverse().ToArray());
        using (raw)
        {
            Assert.Equal(["a", "b", "c"], (await store.ListAsync(new WorkflowDefinitionFilter())).Select(x => x.Id));
            AssertRoute(raw, WorkflowsDesignStorageManifest.DefinitionByIdIndex,
                [WorkflowsDesignStorageManifest.DefinitionIdField]);
        }
    }

    [Fact]
    public async Task List_with_no_filter_returns_all()
    {
        var (store, raw) = Seeded(Sample());
        using (raw)
        {
            var result = await store.ListAsync(new WorkflowDefinitionFilter());
            Assert.Equal(["a", "b", "c"], result.Select(x => x.Id).OrderBy(x => x, StringComparer.Ordinal));
            Assert.Equal(1, raw.LoadCount);
            AssertRoute(raw, WorkflowsDesignStorageManifest.DefinitionByIdIndex,
                [WorkflowsDesignStorageManifest.DefinitionIdField]);
        }
    }

    [Fact]
    public async Task Empty_store_returns_nothing()
    {
        var (store, raw) = Seeded();
        using (raw)
            Assert.Empty(await store.ListAsync(new WorkflowDefinitionFilter()));
    }

    [Fact]
    public async Task List_uses_the_selected_named_route_with_its_complete_declared_order()
    {
        using var raw = new DesignGroundworkTestPersistence { RecordQueries = true };
        foreach (var definition in Sample())
            raw.SeedDefinition(definition);

        var store = new GroundworkWorkflowDefinitionStore(raw, DesignGroundworkTestAccess.DefaultAccessContextAccessor);
        var result = await store.ListAsync(new WorkflowDefinitionFilter
        {
            SearchTerm = "order",
            Description = "Handles orders"
        });

        Assert.Equal(["a"], result.Select(x => x.Id));
        Assert.Single(raw.Queries);
        var query = Assert.Single(raw.Queries, query => query.Options?.SelectedIndex == WorkflowsDesignStorageManifest.DefinitionByDescriptionIndex);
        Assert.Null(query.Request.AcceptedScan);
        Assert.Equal(WorkflowsDesignStorageManifest.DefinitionByDescriptionIndex, query.IndexName);
        Assert.Equal(
            [WorkflowsDesignStorageManifest.DefinitionDescriptionLookupHashField,
             WorkflowsDesignStorageManifest.DefinitionIdField],
            query.Request.Order.Select(x => x.Column.Name));
    }

    [Fact]
    public async Task Combined_exact_filters_use_one_bounded_route_and_preserve_ordinal_residuals()
    {
        var (store, raw) = Seeded(
            new WorkflowDefinition { Id = "a", Name = "Order Processing", Description = "Handles orders" },
            new WorkflowDefinition { Id = "b", Name = "Order Processing", Description = "Other" },
            new WorkflowDefinition { Id = "c", Name = "Other", Description = "Handles orders" });

        using (raw)
        {
            var result = await store.ListAsync(new WorkflowDefinitionFilter
            {
                Name = "Order Processing",
                Description = "Handles orders"
            });

            Assert.Equal(["a"], result.Select(definition => definition.Id));
            AssertRoute(raw, WorkflowsDesignStorageManifest.DefinitionByDescriptionIndex,
                [WorkflowsDesignStorageManifest.DefinitionDescriptionLookupHashField,
                 WorkflowsDesignStorageManifest.DefinitionIdField]);
            Assert.DoesNotContain(raw.Queries, query => query.Request.AcceptedScan is not null);
        }
    }

    [Fact]
    public async Task Tenant_agnostic_query_requires_explicit_across_scope_privilege_and_records_outcome()
    {
        using var raw = new DesignGroundworkTestPersistence();
        raw.SeedDefinition(new WorkflowDefinition { Id = "a", TenantId = "tenant-a", Name = "A" });
        raw.SeedDefinition(new WorkflowDefinition { Id = "b", TenantId = "tenant-b", Name = "B" });
        var accessor = DesignGroundworkTestAccess.Mutable(
            PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var store = new GroundworkWorkflowDefinitionStore(raw, accessor);
        var filter = new WorkflowDefinitionFilter { TenantAgnostic = true };

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ListAsync(filter));
        Assert.Equal(0, raw.LoadCount);

        accessor.Current = PersistenceAccessContext.PrivilegedScoped(
            new PersistenceScope("tenant-a"),
            new PersistenceAccessPurpose("list-workflow-definitions-across-tenants"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ListAsync(filter));
        Assert.Equal(0, raw.LoadCount);

        accessor.Current = PersistenceAccessContext.PrivilegedAcrossScopes(
            new PersistenceAccessPurpose("list-workflow-definitions-across-tenants"));
        var sink = new GroundworkPrivilegedQueryAuditSink();
        store = new GroundworkWorkflowDefinitionStore(raw, accessor, auditSink: sink);
        var result = await store.ListAsync(filter);
        Assert.Equal(["a", "b"], result.Select(x => x.Id));
        Assert.Equal(1, raw.LoadCount);
        Assert.True(Assert.Single(raw.OpenedAccesses).IsPrivilegedAcrossScopes);
        var audit = sink.Snapshot();
        Assert.Equal(2, audit.Count);
        Assert.Equal(GroundworkPrivilegedQueryAuditEventKind.Acquisition, audit[0].EventKind);
        Assert.Equal(GroundworkPrivilegedQueryAuditEventKind.Outcome, audit[1].EventKind);
        Assert.Equal(audit[0].AcquisitionId, audit[1].AcquisitionId);
        Assert.Equal(GroundworkPrivilegedQueryOutcome.Succeeded, audit[1].Outcome);
        Assert.Equal("query-workflow-design-across-scopes", audit[0].AuditIdentity);
        Assert.Equal("list-workflow-definitions-across-tenants", audit[0].Purpose);
    }

    [Fact]
    public async Task Scope_mismatch_is_rejected()
    {
        var (store, raw) = Seeded(new WorkflowDefinition { Id = "a", TenantId = "tenant-a", Name = "A" });
        using (raw)
        {
            var scoped = new GroundworkWorkflowDefinitionStore(raw, DesignGroundworkTestAccess.AccessContext("tenant-b"));
            Assert.Null(await scoped.FindByIdAsync("a"));
        }
    }

    [Fact]
    public async Task Search_matches_id_case_insensitively()
    {
        var (store, raw) = Seeded(new WorkflowDefinition { Id = "Alpha", Name = "Other" });
        raw.RecordQueries = true;
        using (raw)
        {
            var result = await store.ListAsync(new WorkflowDefinitionFilter { SearchTerm = "ALP" });
            Assert.Equal("Alpha", result.Single().Id);
        }
    }

    [Theory]
    [InlineData("Café", "CAFÉ", true)]
    [InlineData("𐐨", "𐐀", true)]
    [InlineData("Straße", "STRAẞE", false)]
    public async Task Search_id_uses_provider_independent_unicode_case_identity(
        string storedId,
        string searchTerm,
        bool expectedMatch)
    {
        var (store, raw) = Seeded(new WorkflowDefinition { Id = storedId, Name = "Other" });
        using (raw)
        {
            var result = await store.ListAsync(new WorkflowDefinitionFilter { SearchTerm = searchTerm });
            Assert.Equal(expectedMatch ? [storedId] : [], result.Select(x => x.Id));
        }
    }

    [Fact]
    public async Task Privileged_cross_scope_search_deduplicates_by_provider_scope_and_id()
    {
        using var raw = new DesignGroundworkTestPersistence();
        InsertDefinition(raw, "same", "tenant-a", null, "A");
        InsertDefinition(raw, "same", "tenant-b", "forged-tenant", "B");
        var accessor = DesignGroundworkTestAccess.Mutable(
            PersistenceAccessContext.PrivilegedAcrossScopes(
                new PersistenceAccessPurpose("workflow-design-cross-scope-test")));
        var store = new GroundworkWorkflowDefinitionStore(raw, accessor, auditSink: new GroundworkPrivilegedQueryAuditSink());

        var result = await store.ListAsync(new WorkflowDefinitionFilter { TenantAgnostic = true });

        Assert.Equal(2, result.Count);
        Assert.Equal([null, "forged-tenant"], result.Select(definition => definition.TenantId));
        Assert.Equal(["A", "B"], result.Select(definition => definition.Name));
    }

    [Fact]
    public void Privileged_cross_scope_point_read_refuses_ambiguous_same_id()
    {
        using var raw = new DesignGroundworkTestPersistence();
        InsertDefinition(raw, "same", "tenant-a", null, "A");
        InsertDefinition(raw, "same", "tenant-b", "forged-tenant", "B");
        var accessor = DesignGroundworkTestAccess.Mutable(
            PersistenceAccessContext.PrivilegedAcrossScopes(
                new PersistenceAccessPurpose("workflow-design-cross-scope-point-read-test")));
        var storage = new GroundworkDesignStorage(raw, accessor, auditSink: new GroundworkPrivilegedQueryAuditSink());

        var exception = Assert.Throws<GroundworkQueryReadinessException>(() => storage.Read(
            WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind,
            "same",
            acrossScopes: true));
        Assert.Contains("ambiguous", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static void InsertDefinition(
        DesignGroundworkTestPersistence raw,
        string id,
        string scope,
        string? payloadTenant,
        string name)
    {
        var definition = new WorkflowDefinition { Id = id, TenantId = payloadTenant, Name = name };
        var options = GroundworkDesignDocumentSerialization.Create(new FakePayloadSerializer());
        raw.InsertRaw(
            WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind,
            GroundworkDesignStorage.Values(
                WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind,
                definition,
                options,
                WorkflowsDesignStorageManifest.WorkflowDefinitionCollection),
            scope);
    }

    private sealed class ThrowingSource(IGroundworkStorageSessionSource inner, Exception failure) : IGroundworkStorageSessionSource, IGroundworkStorageCapabilitySource
    {
        public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null) => throw failure;
        public IUnitOfWork BeginUnitOfWork(StorageAccess access, BatchWriteOptions options, IReadOnlyList<string> unitIds, string? targetName = null) => inner.BeginUnitOfWork(access, options, unitIds, targetName);
        public StorageUnit Unit(string unitId, string? targetName = null) => inner.Unit(unitId, targetName);
        public IReadOnlyList<CapabilityDescriptor> Capabilities(string? targetName = null) =>
            ((IGroundworkStorageCapabilitySource)inner).Capabilities(targetName);
    }

    private static void AssertRoute(
        DesignGroundworkTestPersistence raw,
        string index,
        IReadOnlyList<string> order)
    {
        var query = Assert.Single(raw.Queries.TakeLast(1));
        Assert.Equal(index, query.IndexName);
        Assert.Equal(index, query.Options!.SelectedIndex);
        Assert.Equal(order, query.Request.Order.Select(term => term.Column.Name));
    }
}
