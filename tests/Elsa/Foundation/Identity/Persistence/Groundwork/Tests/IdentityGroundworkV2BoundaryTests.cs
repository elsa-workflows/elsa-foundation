using Elsa.Foundation.Identity.Persistence.Groundwork.Stores;
using Elsa.Persistence.Groundwork.Composition;
using Groundwork.Kernel;
using Groundwork.Store;
using Groundwork.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Foundation.Identity.Persistence.Groundwork.Tests;

public sealed class IdentityGroundworkV2BoundaryTests
{
    [Fact]
    public void Fresh_v2_schema_preserves_all_identity_units_and_authority_indexes()
    {
        var units = IdentityV2StorageManifest.CreateUnits();

        Assert.Equal(17, units.Count);
        Assert.Equal(16, units.Count(unit => unit.Scope == ScopePolicy.Scoped));
        var global = Assert.Single(units, unit => unit.Scope == ScopePolicy.Global);
        Assert.Equal(IdentityStorageManifest.IdentityGlobalProviderConfigurationDocumentKind, global.Id.Value);
        Assert.All(units, unit =>
        {
            Assert.True(unit.Concurrency.IsOptimistic);
            Assert.Equal(PortableType.String, unit.Columns.Single(column => column.Name == IdentityV2StorageManifest.IdField).Type);
            Assert.Equal(PortableType.String, unit.Columns.Single(column => column.Name == IdentityV2StorageManifest.SchemaVersionField).Type);
            Assert.Equal(PortableType.Json, unit.Columns.Single(column => column.Name == IdentityV2StorageManifest.ContentField).Type);
            Assert.Equal(IdentityV2StorageManifest.IdField, Assert.Single(unit.Key.Columns));
        });

        var users = IdentityV2StorageManifest.Require(IdentityStorageManifest.IdentityUserDocumentKind);
        Assert.Contains(users.Indexes, index =>
            index.Columns.SequenceEqual([new IndexColumn(IdentityStorageManifest.NormalizedUserNameKeyField)]));
        Assert.Contains(users.Indexes, index =>
            index.Columns.SequenceEqual([new IndexColumn(IdentityStorageManifest.NormalizedEmailKeyField)]));
        Assert.True(users.Columns.Single(column => column.Name == IdentityStorageManifest.NormalizedUserNameKeyField).IsNullable);
        Assert.True(users.Columns.Single(column => column.Name == IdentityStorageManifest.NormalizedEmailKeyField).IsNullable);

        var receipts = IdentityV2StorageManifest.Require(IdentityStorageManifest.IdentityMutationReceiptDocumentKind);
        Assert.Equal(
            PortableType.DateTimeOffset,
            receipts.Columns.Single(column => column.Name == IdentityStorageManifest.MutationReceiptExpiresAtField).Type);
        Assert.Contains(receipts.Indexes, index =>
            index.Columns.SequenceEqual([new IndexColumn(IdentityStorageManifest.MutationReceiptExpiresAtField)]));
    }

    [Fact]
    public void Production_identity_persistence_references_only_the_public_v2_groundwork_surface()
    {
        var references = typeof(GroundworkIdentityAuthorityAggregateCoordinator)
            .Assembly
            .GetReferencedAssemblies();

        Assert.DoesNotContain(references, reference =>
            reference.Name is "Groundwork.Core" or "Groundwork.Documents");
        Assert.Contains(references, reference => reference.Name == "Groundwork.Kernel");
        Assert.Contains(references, reference => reference.Name == "Groundwork.Query.Model");
        Assert.Contains(references, reference => reference.Name == "Groundwork.Store");
    }

    [Fact]
    public async Task Shipping_session_source_reuses_one_provider_session_per_unit_and_access()
    {
        var unit = IdentityV2StorageManifest.Require(IdentityStorageManifest.IdentityUserDocumentKind);
        using var inner = new InMemoryProviderFactory().Create($"identity-session-source:{Guid.NewGuid():N}");
        var connection = new CountingProviderConnection(inner);
        var registry = new GroundworkStorageUnitRegistry();
        registry.Declare(unit);
        using var services = new ServiceCollection()
            .AddSingleton<IStorageProviderConnection>(connection)
            .BuildServiceProvider();
        var source = new GroundworkStorageSessionSource(services, registry);

        var sameAccess = Enumerable.Range(0, 64)
            .Select(_ => StorageAccess.Scoped(new StorageScope("tenant-a")))
            .ToArray();
        var sessions = await Task.WhenAll(sameAccess.Select(access =>
            Task.Run(() => source.Open(unit.Id.Value, access))));

        Assert.All(sessions, session => Assert.Same(sessions[0], session));
        Assert.Equal(1, connection.OpenSessionCount);

        var other = source.Open(unit.Id.Value, StorageAccess.Scoped(new StorageScope("tenant-b")));

        Assert.NotSame(sessions[0], other);
        Assert.Equal(2, connection.OpenSessionCount);
    }

    private sealed class CountingProviderConnection(IStorageProviderConnection inner)
        : IStorageProviderConnection
    {
        private int openSessionCount;

        public int OpenSessionCount => Volatile.Read(ref openSessionCount);
        public IProviderCatalog Catalog => inner.Catalog;
        public ISchemaCoordinator Schema => inner.Schema;
        public IReadOnlyList<CapabilityDescriptor> Capabilities => inner.Capabilities;

        public IStorageSession OpenSession(StorageUnit unit, StorageAccess access)
        {
            Interlocked.Increment(ref openSessionCount);
            return inner.OpenSession(unit, access);
        }

        public IUnitOfWork BeginUnitOfWork(StorageAccess access, params StorageUnit[] units) =>
            inner.BeginUnitOfWork(access, units);

        public IUnitOfWork BeginUnitOfWork(
            StorageAccess access,
            BatchWriteOptions options,
            params StorageUnit[] units) =>
            inner.BeginUnitOfWork(access, options, units);

        public void Dispose()
        {
            // The test owns and disposes the wrapped connection.
        }
    }
}
