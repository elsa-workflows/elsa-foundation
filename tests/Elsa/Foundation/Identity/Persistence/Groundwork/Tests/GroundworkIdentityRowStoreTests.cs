using Elsa.Foundation.Identity.Persistence.Groundwork.Stores;
using Elsa.Foundation.Identity.Persistence.Groundwork.Exceptions;
using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Testing;
using Groundwork.Kernel;
using Groundwork.Query.Model;
using Groundwork.Store;
using Groundwork.Testing;

namespace Elsa.Foundation.Identity.Persistence.Groundwork.Tests;

public sealed class GroundworkIdentityRowStoreTests
{
    [Fact]
    public void Scope_and_global_mismatches_are_rejected_before_provider_io()
    {
        using var fixture = Fixture.Create();
        var source = fixture.Source;
        var store = new GroundworkIdentityRowStore(source, fixture.Access);

        fixture.Access.Current = PersistenceAccessContext.Global;
        var scopedError = Assert.Throws<InvalidOperationException>(() => store.Read(
            IdentityStorageManifest.IdentityUserDocumentKind,
            "user-1"));
        Assert.Contains("scoped", scopedError.Message, StringComparison.Ordinal);
        Assert.Equal(0, source.OpenCalls);

        fixture.Access.Current = PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"));
        var globalError = Assert.Throws<InvalidOperationException>(() => store.Read(
            IdentityStorageManifest.IdentityGlobalProviderConfigurationDocumentKind,
            "configuration"));
        Assert.Contains("global", globalError.Message, StringComparison.Ordinal);
        Assert.Equal(0, source.OpenCalls);
    }

    [Fact]
    public void Typed_rows_use_exact_json_and_optimistic_cas_without_cross_scope_reads()
    {
        using var fixture = Fixture.Create();
        var store = new GroundworkIdentityRowStore(fixture.Source, fixture.Access);
        const string json = "{\"displayName\":\"A\",\"roles\":[\"admin\"]}";

        var created = store.Save(new GroundworkIdentityRowWrite(
            IdentityStorageManifest.IdentityUserDocumentKind,
            "user-1",
            json,
            new Dictionary<string, object?>
            {
                [IdentityStorageManifest.NormalizedUserNameKeyField] = "alice"
            },
            GroundworkIdentityRowWriteCondition.CreateOnly));
        Assert.Equal(WriteOutcomeStatus.Inserted, created.Status);
        Assert.Equal(1, created.Version);

        var loaded = store.Read(IdentityStorageManifest.IdentityUserDocumentKind, "user-1");
        Assert.NotNull(loaded);
        Assert.Equal(json, loaded!.CanonicalJson);
        Assert.Equal(IdentityStorageManifest.SchemaVersion, loaded.SchemaVersion);
        Assert.Equal(1, loaded.Version);
        Assert.Equal("alice", loaded.ProjectedValues[IdentityStorageManifest.NormalizedUserNameKeyField]);

        var updated = store.Save(new GroundworkIdentityRowWrite(
            IdentityStorageManifest.IdentityUserDocumentKind,
            "user-1",
            "{\"displayName\":\"B\"}",
            new Dictionary<string, object?>
            {
                [IdentityStorageManifest.NormalizedUserNameKeyField] = "alice-updated"
            },
            GroundworkIdentityRowWriteCondition.IfVersion(1)));
        Assert.True(updated.Succeeded);
        Assert.Equal(2, updated.Version);

        var stale = store.Save(new GroundworkIdentityRowWrite(
            IdentityStorageManifest.IdentityUserDocumentKind,
            "user-1",
            "{\"displayName\":\"stale\"}",
            new Dictionary<string, object?>(),
            GroundworkIdentityRowWriteCondition.IfVersion(1)));
        Assert.Equal(WriteOutcomeStatus.ConcurrencyConflict, stale.Status);

        fixture.Access.Current = PersistenceAccessContext.Scoped(new PersistenceScope("tenant-b"));
        Assert.Null(store.Read(IdentityStorageManifest.IdentityUserDocumentKind, "user-1"));
        Assert.Equal(WriteOutcomeStatus.Inserted, store.Save(new GroundworkIdentityRowWrite(
            IdentityStorageManifest.IdentityUserDocumentKind,
            "user-1",
            "{\"displayName\":\"B\"}",
            new Dictionary<string, object?>(),
            GroundworkIdentityRowWriteCondition.CreateOnly)).Status);
    }

    [Fact]
    public void Equality_and_range_queries_use_the_declared_unit_name_and_stable_order()
    {
        using var fixture = Fixture.Create();
        var store = new GroundworkIdentityRowStore(fixture.Source, fixture.Access);
        SaveRole(store, "r-3", "zulu");
        SaveRole(store, "r-1", "alpha");
        SaveRole(store, "r-2", "mike");
        fixture.Access.Current = PersistenceAccessContext.Scoped(new PersistenceScope("tenant-b"));
        SaveRole(store, "r-b", "bravo");
        fixture.Access.Current = PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"));

        var equal = store.Query(
            IdentityStorageManifest.IdentityRoleDocumentKind,
            new GroundworkIdentityRowQuery(
                IdentityStorageManifest.TenantIdField,
                GroundworkIdentityRowComparison.Equal,
                "tenant-a",
                IdentityStorageManifest.NormalizedRoleNameKeyField,
                IncludeVersions: true));
        Assert.Equal(["alpha", "mike", "zulu"], equal.Select(row => row.ProjectedValues[IdentityStorageManifest.NormalizedRoleNameKeyField]));
        Assert.All(equal, row => Assert.Equal(1, row.Version));

        var range = store.Query(
            IdentityStorageManifest.IdentityRoleDocumentKind,
            new GroundworkIdentityRowQuery(
                IdentityStorageManifest.NormalizedRoleNameKeyField,
                GroundworkIdentityRowComparison.GreaterThanOrEqual,
                "mike",
                IdentityStorageManifest.NormalizedRoleNameKeyField));
        Assert.Equal(["mike", "zulu"], range.Select(row => row.ProjectedValues[IdentityStorageManifest.NormalizedRoleNameKeyField]));

        var offsetCompatiblePage = store.Query(
            IdentityStorageManifest.IdentityRoleDocumentKind,
            new GroundworkIdentityRowQuery(
                IdentityStorageManifest.TenantIdField,
                GroundworkIdentityRowComparison.Equal,
                "tenant-a",
                IdentityStorageManifest.NormalizedRoleNameKeyField,
                Take: 1,
                Skip: 2));
        Assert.Equal("zulu", Assert.Single(offsetCompatiblePage).ProjectedValues[IdentityStorageManifest.NormalizedRoleNameKeyField]);
    }

    [Fact]
    public void Cursor_pages_materialize_each_matching_identity_once_with_a_bounded_limit()
    {
        using var fixture = Fixture.Create();
        var store = new GroundworkIdentityRowStore(fixture.Source, fixture.Access);
        SaveRole(store, "r-3", "zulu");
        SaveRole(store, "r-1", "alpha");
        SaveRole(store, "r-4", "tango");
        SaveRole(store, "r-2", "mike");
        SaveRole(store, "r-5", "yankee");

        var result = store.QueryAllPages(
            IdentityStorageManifest.IdentityRoleDocumentKind,
            new GroundworkIdentityRowQuery(
                IdentityStorageManifest.TenantIdField,
                GroundworkIdentityRowComparison.Equal,
                "tenant-a",
                IdentityStorageManifest.NormalizedRoleNameKeyField,
                Take: 2,
                ExpectedIndex: IdentityV2StorageManifest.RoleByTenantIndex),
            maximumMaterialization: 5);

        Assert.Equal(["alpha", "mike", "tango", "yankee", "zulu"], result.Rows.Select(row =>
            row.ProjectedValues[IdentityStorageManifest.NormalizedRoleNameKeyField]));
        Assert.Equal(5, result.TotalCount);
        Assert.Null(result.NextContinuationToken);

        var boundedError = Assert.Throws<GroundworkIdentityStoreException>(() => store.QueryAllPages(
            IdentityStorageManifest.IdentityRoleDocumentKind,
            new GroundworkIdentityRowQuery(
                IdentityStorageManifest.TenantIdField,
                GroundworkIdentityRowComparison.Equal,
                "tenant-a",
                IdentityStorageManifest.NormalizedRoleNameKeyField,
                Take: 2),
            maximumMaterialization: 3));
        Assert.Contains("bounded materialization", boundedError.Message, StringComparison.Ordinal);

        var offsetPage = store.QueryWithTotalCount(
            IdentityStorageManifest.IdentityRoleDocumentKind,
            new GroundworkIdentityRowQuery(
                IdentityStorageManifest.TenantIdField,
                GroundworkIdentityRowComparison.Equal,
                "tenant-a",
                IdentityStorageManifest.NormalizedRoleNameKeyField,
                Take: 3,
                Skip: 1,
                ExpectedIndex: IdentityV2StorageManifest.RoleByTenantIndex));
        Assert.Equal(["mike", "tango", "yankee"], offsetPage.Rows.Select(row =>
            row.ProjectedValues[IdentityStorageManifest.NormalizedRoleNameKeyField]));
        Assert.NotNull(offsetPage.NextContinuationToken);

        var nextPage = store.QueryAllPages(
            IdentityStorageManifest.IdentityRoleDocumentKind,
            new GroundworkIdentityRowQuery(
                IdentityStorageManifest.TenantIdField,
                GroundworkIdentityRowComparison.Equal,
                "tenant-a",
                IdentityStorageManifest.NormalizedRoleNameKeyField,
                Take: 3,
                ExpectedIndex: IdentityV2StorageManifest.RoleByTenantIndex,
                ContinuationToken: offsetPage.NextContinuationToken),
            maximumMaterialization: 1);
        Assert.Equal(["zulu"], nextPage.Rows.Select(row =>
            row.ProjectedValues[IdentityStorageManifest.NormalizedRoleNameKeyField]));

        Assert.Throws<ArgumentException>(() => store.QueryWithTotalCount(
            IdentityStorageManifest.IdentityRoleDocumentKind,
            new GroundworkIdentityRowQuery(
                IdentityStorageManifest.TenantIdField,
                GroundworkIdentityRowComparison.Equal,
                "tenant-a",
                IdentityStorageManifest.NormalizedRoleNameKeyField,
                Take: 1,
                Skip: 1,
                ContinuationToken: offsetPage.NextContinuationToken)));
    }

    [Fact]
    public void Cursor_query_honors_cancellation_before_opening_a_provider_session()
    {
        using var fixture = Fixture.Create();
        var store = new GroundworkIdentityRowStore(fixture.Source, fixture.Access);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => store.QueryAllPages(
            IdentityStorageManifest.IdentityRoleDocumentKind,
            new GroundworkIdentityRowQuery(
                IdentityStorageManifest.TenantIdField,
                GroundworkIdentityRowComparison.Equal,
                "tenant-a",
                IdentityStorageManifest.NormalizedRoleNameKeyField,
                Take: 2),
            maximumMaterialization: 4,
            cancellationToken: cancellation.Token));
        Assert.Equal(0, fixture.Source.OpenCalls);
    }

    [Fact]
    public void Cursor_query_honors_cancellation_after_the_first_provider_page()
    {
        using var fixture = Fixture.Create();
        using var cancellation = new CancellationTokenSource();
        var queryCalls = 0;
        fixture.Source.QueryOverride = _ =>
        {
            queryCalls++;
            cancellation.Cancel();
            return new QueryMaterializedResult([IdentityValues("r-1")], null, "next");
        };
        var store = new GroundworkIdentityRowStore(fixture.Source, fixture.Access);

        Assert.Throws<OperationCanceledException>(() => store.QueryAllPages(
            IdentityStorageManifest.IdentityRoleDocumentKind,
            new GroundworkIdentityRowQuery(
                IdentityStorageManifest.TenantIdField,
                GroundworkIdentityRowComparison.Equal,
                "tenant-a",
                IdentityV2StorageManifest.IdField,
                Take: 1),
            maximumMaterialization: 4,
            cancellationToken: cancellation.Token));
        Assert.Equal(1, queryCalls);
    }

    [Fact]
    public void Offset_compatibility_honors_cancellation_before_following_the_next_provider_page()
    {
        using var fixture = Fixture.Create();
        using var cancellation = new CancellationTokenSource();
        var queryCalls = 0;
        fixture.Source.QueryOverride = _ =>
        {
            queryCalls++;
            cancellation.Cancel();
            return new QueryMaterializedResult([IdentityValues("r-1")], null, "next");
        };
        var store = new GroundworkIdentityRowStore(fixture.Source, fixture.Access);

        Assert.Throws<OperationCanceledException>(() => store.QueryWithTotalCount(
            IdentityStorageManifest.IdentityRoleDocumentKind,
            new GroundworkIdentityRowQuery(
                IdentityStorageManifest.TenantIdField,
                GroundworkIdentityRowComparison.Equal,
                "tenant-a",
                IdentityV2StorageManifest.IdField,
                Take: 1,
                Skip: 1),
            cancellation.Token));
        Assert.Equal(1, queryCalls);
    }

    [Fact]
    public void Cursor_query_rejects_empty_repeated_and_duplicate_identity_progress()
    {
        using var emptyFixture = Fixture.Create();
        emptyFixture.Source.QueryOverride = _ => new QueryMaterializedResult([], null, " ");
        var emptyError = Assert.Throws<GroundworkIdentityStoreException>(() => new GroundworkIdentityRowStore(
            emptyFixture.Source,
            emptyFixture.Access).QueryAllPages(
                IdentityStorageManifest.IdentityRoleDocumentKind,
                new GroundworkIdentityRowQuery(
                    IdentityStorageManifest.TenantIdField,
                    GroundworkIdentityRowComparison.Equal,
                    "tenant-a",
                    IdentityV2StorageManifest.IdField,
                    Take: 1),
                maximumMaterialization: 4));
        Assert.Contains("empty", emptyError.Message, StringComparison.Ordinal);

        using var noProgressFixture = Fixture.Create();
        noProgressFixture.Source.QueryOverride = _ => new QueryMaterializedResult([], null, "next");
        var noProgressError = Assert.Throws<GroundworkIdentityStoreException>(() => new GroundworkIdentityRowStore(
            noProgressFixture.Source,
            noProgressFixture.Access).QueryAllPages(
                IdentityStorageManifest.IdentityRoleDocumentKind,
                new GroundworkIdentityRowQuery(
                    IdentityStorageManifest.TenantIdField,
                    GroundworkIdentityRowComparison.Equal,
                    "tenant-a",
                    IdentityV2StorageManifest.IdField,
                    Take: 1),
                maximumMaterialization: 4));
        Assert.Contains("forward progress", noProgressError.Message, StringComparison.Ordinal);

        using var repeatedFixture = Fixture.Create();
        var repeatedCalls = 0;
        repeatedFixture.Source.QueryOverride = _ => new QueryMaterializedResult(
            [IdentityValues($"r-{++repeatedCalls}")],
            null,
            "repeat");
        var repeatedError = Assert.Throws<GroundworkIdentityStoreException>(() => new GroundworkIdentityRowStore(
            repeatedFixture.Source,
            repeatedFixture.Access).QueryAllPages(
                IdentityStorageManifest.IdentityRoleDocumentKind,
                new GroundworkIdentityRowQuery(
                    IdentityStorageManifest.TenantIdField,
                    GroundworkIdentityRowComparison.Equal,
                    "tenant-a",
                    IdentityV2StorageManifest.IdField,
                    Take: 1),
                maximumMaterialization: 4));
        Assert.Contains("repeated", repeatedError.Message, StringComparison.Ordinal);

        using var duplicateFixture = Fixture.Create();
        var duplicateCalls = 0;
        duplicateFixture.Source.QueryOverride = _ =>
        {
            duplicateCalls++;
            return new QueryMaterializedResult(
                [IdentityValues("r-1")],
                null,
                duplicateCalls == 1 ? "next" : null);
        };
        var duplicateError = Assert.Throws<GroundworkIdentityStoreException>(() => new GroundworkIdentityRowStore(
            duplicateFixture.Source,
            duplicateFixture.Access).QueryAllPages(
                IdentityStorageManifest.IdentityRoleDocumentKind,
                new GroundworkIdentityRowQuery(
                    IdentityStorageManifest.TenantIdField,
                    GroundworkIdentityRowComparison.Equal,
                    "tenant-a",
                    IdentityV2StorageManifest.IdField,
                    Take: 1),
                maximumMaterialization: 4));
        Assert.Contains("repeated row", duplicateError.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Cursor_query_enforces_the_page_limit_when_each_page_makes_progress()
    {
        using var fixture = Fixture.Create();
        var queryCalls = 0;
        fixture.Source.QueryOverride = _ => new QueryMaterializedResult(
            [IdentityValues($"r-{++queryCalls}")],
            null,
            "next-" + queryCalls);
        var store = new GroundworkIdentityRowStore(fixture.Source, fixture.Access);

        var error = Assert.Throws<GroundworkIdentityStoreException>(() => store.QueryAllPages(
            IdentityStorageManifest.IdentityRoleDocumentKind,
            new GroundworkIdentityRowQuery(
                IdentityStorageManifest.TenantIdField,
                GroundworkIdentityRowComparison.Equal,
                "tenant-a",
                IdentityV2StorageManifest.IdField,
                Take: 1),
            maximumMaterialization: 2));

        Assert.Contains("page limit", error.Message, StringComparison.Ordinal);
        Assert.Equal(2, queryCalls);
    }

    [Fact]
    public void Offset_compatibility_stops_on_an_empty_provider_page()
    {
        using var fixture = Fixture.Create();
        fixture.Source.QueryOverride = _ => new QueryMaterializedResult([], 0, null);
        var store = new GroundworkIdentityRowStore(fixture.Source, fixture.Access);

        var result = store.QueryWithTotalCount(
            IdentityStorageManifest.IdentityRoleDocumentKind,
            new GroundworkIdentityRowQuery(
                IdentityStorageManifest.TenantIdField,
                GroundworkIdentityRowComparison.Equal,
                "tenant-a",
                IdentityV2StorageManifest.IdField,
                Take: 1,
                Skip: 3));

        Assert.Empty(result.Rows);
        Assert.Equal(0, result.TotalCount);
        Assert.Null(result.NextContinuationToken);
    }

    [Fact]
    public void Offset_compatibility_consumes_multiple_non_empty_pages_before_collecting()
    {
        using var fixture = Fixture.Create();
        var queryCalls = 0;
        fixture.Source.QueryOverride = _ =>
        {
            queryCalls++;
            return new QueryMaterializedResult(
                [IdentityValues($"r-{queryCalls}")],
                3,
                queryCalls < 3 ? $"next-{queryCalls}" : null);
        };
        var store = new GroundworkIdentityRowStore(fixture.Source, fixture.Access);

        var result = store.QueryWithTotalCount(
            IdentityStorageManifest.IdentityRoleDocumentKind,
            new GroundworkIdentityRowQuery(
                IdentityStorageManifest.TenantIdField,
                GroundworkIdentityRowComparison.Equal,
                "tenant-a",
                IdentityV2StorageManifest.IdField,
                Take: 1,
                Skip: 2));

        Assert.Equal("r-3", Assert.Single(result.Rows).Id);
        Assert.Equal(3, result.TotalCount);
        Assert.Null(result.NextContinuationToken);
        Assert.Equal(3, queryCalls);
    }

    [Fact]
    public void Cursor_query_wraps_provider_failure_in_the_identity_store_exception_contract()
    {
        using var fixture = Fixture.Create();
        fixture.Source.QueryOverride = _ => throw new InvalidOperationException("provider failed");
        var store = new GroundworkIdentityRowStore(fixture.Source, fixture.Access);
        var query = new GroundworkIdentityRowQuery(
            IdentityStorageManifest.TenantIdField,
            GroundworkIdentityRowComparison.Equal,
            "tenant-a",
            IdentityV2StorageManifest.IdField,
            Take: 1);

        var pageError = Assert.Throws<GroundworkIdentityStoreException>(() => store.Query(
            IdentityStorageManifest.IdentityRoleDocumentKind,
            query));
        Assert.Equal("provider failed", pageError.InnerException?.Message);

        var totalError = Assert.Throws<GroundworkIdentityStoreException>(() => store.QueryWithTotalCount(
            IdentityStorageManifest.IdentityRoleDocumentKind,
            query));
        Assert.Equal("provider failed", totalError.InnerException?.Message);

        var error = Assert.Throws<GroundworkIdentityStoreException>(() => store.QueryAllPages(
            IdentityStorageManifest.IdentityRoleDocumentKind,
            query,
            maximumMaterialization: 4));
        Assert.Equal("provider failed", error.InnerException?.Message);
    }

    [Fact]
    public void Total_count_protocol_failure_uses_the_identity_store_exception_contract()
    {
        using var fixture = Fixture.Create();
        fixture.Source.QueryOverride = _ => new QueryMaterializedResult([], null, null);
        var store = new GroundworkIdentityRowStore(fixture.Source, fixture.Access);
        var query = new GroundworkIdentityRowQuery(
            IdentityStorageManifest.TenantIdField,
            GroundworkIdentityRowComparison.Equal,
            "tenant-a",
            IdentityV2StorageManifest.IdField,
            Take: 1);

        var error = Assert.Throws<GroundworkIdentityStoreException>(() => store.QueryWithTotalCount(
            IdentityStorageManifest.IdentityRoleDocumentKind,
            query));

        Assert.IsType<InvalidDataException>(error.InnerException);
        Assert.Contains("filtered total count", error.InnerException!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Cursor_query_rejects_non_positive_bounds()
    {
        using var fixture = Fixture.Create();
        var store = new GroundworkIdentityRowStore(fixture.Source, fixture.Access);
        var query = new GroundworkIdentityRowQuery(
            IdentityStorageManifest.TenantIdField,
            GroundworkIdentityRowComparison.Equal,
            "tenant-a",
            IdentityV2StorageManifest.IdField);

        Assert.Throws<ArgumentOutOfRangeException>(() => store.QueryAllPages(
            IdentityStorageManifest.IdentityRoleDocumentKind,
            query,
            maximumMaterialization: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => store.QueryAllPages(
            IdentityStorageManifest.IdentityRoleDocumentKind,
            query with { Take = 0 },
            maximumMaterialization: 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => store.QueryAllPages(
            IdentityStorageManifest.IdentityRoleDocumentKind,
            query with { Skip = -1 },
            maximumMaterialization: 4));
    }

    [Fact]
    public void Exact_batch_rolls_back_first_write_when_a_later_cas_loses()
    {
        using var fixture = Fixture.Create(interfereDuringBatch: true);
        var store = new GroundworkIdentityRowStore(fixture.Source, fixture.Access);
        SaveRole(store, "existing", "before");

        var mutations = new[]
        {
            GroundworkIdentityRowMutation.Save(new GroundworkIdentityRowWrite(
                IdentityStorageManifest.IdentityApplicationDocumentKind,
                "first",
                "{\"value\":1}",
                new Dictionary<string, object?>(),
                GroundworkIdentityRowWriteCondition.CreateOnly)),
            GroundworkIdentityRowMutation.Save(new GroundworkIdentityRowWrite(
                IdentityStorageManifest.IdentityRoleDocumentKind,
                "existing",
                "{\"name\":\"batch\"}",
                new Dictionary<string, object?>
                {
                    [IdentityStorageManifest.TenantIdField] = "tenant-a",
                    [IdentityStorageManifest.NormalizedRoleNameKeyField] = "batch"
                },
                GroundworkIdentityRowWriteCondition.IfVersion(1)))
        };

        Assert.Throws<BatchWriteException>(() => store.WriteBatch(mutations));
        Assert.Null(store.Read(IdentityStorageManifest.IdentityApplicationDocumentKind, "first"));
        Assert.Equal("{\"name\":\"interfering\"}", store.Read(
            IdentityStorageManifest.IdentityRoleDocumentKind,
            "existing")!.CanonicalJson);
    }

    [Fact]
    public void Domain_batch_coalesces_repeated_row_changes_against_the_first_observed_version()
    {
        using var fixture = Fixture.Create();
        var store = new GroundworkIdentityRowStore(fixture.Source, fixture.Access);
        Assert.True(store.Save(new GroundworkIdentityRowWrite(
            IdentityStorageManifest.IdentityApplicationDocumentKind,
            "application-1",
            "{\"value\":1}",
            new Dictionary<string, object?>(),
            GroundworkIdentityRowWriteCondition.CreateOnly)).Succeeded);

        var batch = new GroundworkIdentityMutationBatch(store);
        Assert.True(batch.Save(new GroundworkIdentityRowWrite(
            IdentityStorageManifest.IdentityApplicationDocumentKind,
            "application-1",
            "{\"value\":2}",
            new Dictionary<string, object?>(),
            GroundworkIdentityRowWriteCondition.IfVersion(1))).Succeeded);
        Assert.True(batch.Delete(new GroundworkIdentityRowDelete(
            IdentityStorageManifest.IdentityApplicationDocumentKind,
            "application-1",
            GroundworkIdentityRowWriteCondition.IfVersion(2))).Succeeded);

        var report = batch.Commit();

        Assert.True(report.IsSuccessful);
        Assert.Single(report.Outcomes);
        Assert.Null(store.Read(IdentityStorageManifest.IdentityApplicationDocumentKind, "application-1"));
    }

    private static void SaveRole(GroundworkIdentityRowStore store, string id, string normalizedName)
    {
        Assert.Equal(WriteOutcomeStatus.Inserted, store.Save(new GroundworkIdentityRowWrite(
            IdentityStorageManifest.IdentityRoleDocumentKind,
            id,
            $"{{\"name\":\"{normalizedName}\"}}",
            new Dictionary<string, object?>
            {
                [IdentityStorageManifest.TenantIdField] = "tenant-a",
                [IdentityStorageManifest.NormalizedRoleNameKeyField] = normalizedName
            },
            GroundworkIdentityRowWriteCondition.CreateOnly)).Status);
    }

    private static IReadOnlyDictionary<string, object?> IdentityValues(string id) => new Dictionary<string, object?>
    {
        [IdentityV2StorageManifest.IdField] = id,
        [IdentityV2StorageManifest.SchemaVersionField] = IdentityStorageManifest.SchemaVersion,
        [IdentityV2StorageManifest.ContentField] = "{}",
        [IdentityStorageManifest.TenantIdField] = "tenant-a"
    };

    private sealed class Fixture : IDisposable
    {
        private Fixture(DirectSessionSource source, MutableAccessAccessor access)
        {
            Source = source;
            Access = access;
        }

        public DirectSessionSource Source { get; }
        public MutableAccessAccessor Access { get; }

        public static Fixture Create(bool interfereDuringBatch = false)
        {
            var units = IdentityV2StorageManifest.CreateUnits();
            var access = new MutableAccessAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
            return new Fixture(new DirectSessionSource(units, interfereDuringBatch), access);
        }

        public void Dispose() => Source.Dispose();
    }

    private sealed class DirectSessionSource(
        IReadOnlyList<StorageUnit> units,
        bool interfereDuringBatch) : IGroundworkStorageSessionSource
    {
        private readonly IReadOnlyDictionary<string, StorageUnit> units = units.ToDictionary(unit => unit.Id.Value, StringComparer.Ordinal);
        private readonly IStorageProviderConnection connection = CreateConnection(units);
        private bool interfered;

        public int OpenCalls { get; private set; }

        public Func<QueryRequest, QueryMaterializedResult>? QueryOverride { get; set; }

        public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null)
        {
            OpenCalls++;
            var session = connection.OpenSession(units[unitId], access);
            return QueryOverride is null ? session : new QueryOverrideSession(session, QueryOverride);
        }

        public IUnitOfWork BeginUnitOfWork(StorageAccess access, BatchWriteOptions options, IReadOnlyList<string> unitIds, string? targetName = null)
        {
            if (interfereDuringBatch && !interfered)
            {
                interfered = true;
                connection.OpenSession(units[IdentityStorageManifest.IdentityRoleDocumentKind], access)
                    .Upsert(new StorageValues(new Dictionary<string, object?>
                    {
                        [IdentityV2StorageManifest.IdField] = "existing",
                        [IdentityV2StorageManifest.SchemaVersionField] = IdentityStorageManifest.SchemaVersion,
                        [IdentityV2StorageManifest.ContentField] = "{\"name\":\"interfering\"}",
                        [IdentityStorageManifest.TenantIdField] = "tenant-a",
                        [IdentityStorageManifest.NormalizedRoleNameKeyField] = "interfering"
                    }), WriteOptions.Unconditional);
            }

            return connection.BeginUnitOfWork(
                access,
                options,
                unitIds.Select(unitId => units[unitId]).ToArray());
        }

        public StorageUnit Unit(string unitId, string? targetName = null) => units[unitId];

        public void Dispose() => connection.Dispose();

        private static IStorageProviderConnection CreateConnection(IReadOnlyList<StorageUnit> units)
        {
            var connection = new InMemoryProviderFactory().Create($"identity-row-store:{Guid.NewGuid():N}");
            foreach (var unit in units)
                connection.Schema.Apply(unit);
            return connection;
        }
    }

    private sealed class QueryOverrideSession(
        IStorageSession inner,
        Func<QueryRequest, QueryMaterializedResult> queryOverride) : SynchronousStorageSessionTestDouble, IStorageSession
    {
        public StorageUnit Unit => inner.Unit;
        public StorageAccess Access => inner.Access;
        public StoredEntry? Read(StorageKey key) => inner.Read(key);
        public QueryMaterializedResult Query(QueryRequest request, QueryRenderOptions? options = null) => queryOverride(request);
        public WriteOutcome Insert(StorageValues values, WriteOptions? options = null) => inner.Insert(values, options);
        public WriteOutcome Update(StorageValues values, WriteOptions? options = null) => inner.Update(values, options);
        public WriteOutcome Upsert(StorageValues values, WriteOptions? options = null) => inner.Upsert(values, options);
        public WriteOutcome Delete(StorageKey key, WriteOptions? options = null) => inner.Delete(key, options);
        public WriteOutcome Append(OperationId operationId, IReadOnlyList<StorageValues> values) => inner.Append(operationId, values);
        public AggregationResult Aggregate(AggregationQuery query) => inner.Aggregate(query);
    }

    private sealed class MutableAccessAccessor(PersistenceAccessContext current) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; set; } = current;
    }
}
