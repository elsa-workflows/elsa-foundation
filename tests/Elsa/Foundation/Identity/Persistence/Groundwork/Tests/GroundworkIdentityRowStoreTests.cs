using Elsa.Foundation.Identity.Persistence.Groundwork.Stores;
using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Composition;
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

        public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null)
        {
            OpenCalls++;
            return connection.OpenSession(units[unitId], access);
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

    private sealed class MutableAccessAccessor(PersistenceAccessContext current) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; set; } = current;
    }
}
