using Elsa.Foundation.Identity.Persistence.Groundwork.Stores;
using Groundwork.Kernel;

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
}
