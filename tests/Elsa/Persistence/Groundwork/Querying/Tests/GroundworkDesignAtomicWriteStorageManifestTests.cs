using Groundwork.Core.Capabilities;
using Groundwork.Core.Manifests;
using Groundwork.Core.PhysicalStorage;
using Xunit;

namespace Elsa.Persistence.Groundwork.Querying.Tests;

public sealed class GroundworkDesignAtomicWriteStorageManifestTests
{
    [Fact]
    public void Manifest_owns_one_scoped_dedicated_operation_table()
    {
        var manifest = GroundworkDesignAtomicWriteStorageManifest.Create();

        Assert.Equal(GroundworkDesignAtomicWriteStorageManifest.FeatureIdentity, manifest.Identity.Value);
        Assert.Equal(GroundworkDesignAtomicWriteStorageManifest.ManifestOwner, manifest.Owner.Value);
        Assert.Equal(GroundworkDesignAtomicWriteStorageManifest.SchemaVersion, manifest.Version.Value);
        var unit = Assert.Single(manifest.StorageUnits);
        Assert.Equal(GroundworkDesignAtomicWriteStorageManifest.DesignOperationDocumentKind, unit.Identity.Value);
        Assert.Equal(StorageLifecycleKind.AppendOnly, unit.Lifecycle.Kind);
        Assert.Equal(TenancyKind.Scoped, unit.Tenancy.Kind);
        Assert.Equal(ConcurrencyKind.Optimistic, unit.Concurrency.Kind);
        Assert.NotNull(unit.PhysicalStorage);
        var storage = unit.PhysicalStorage!;
        var policy = Assert.IsType<PhysicalStoragePolicy.ExplicitPolicy>(storage.Policy);
        Assert.Equal(PhysicalStorageForm.DedicatedDocumentTable, policy.Definition.Form);
        Assert.Equal(
            GroundworkDesignAtomicWriteStorageManifest.DesignOperationDocumentKind,
            policy.Definition.FeatureDefaultLogicalName);
        Assert.Empty(storage.LogicalIndexes);
        Assert.Empty(storage.BoundedQueries);
    }

    [Fact]
    public async Task Source_requires_atomic_commit_and_transaction_capable_topology()
    {
        var source = new GroundworkDesignAtomicWriteStorageManifestSource();

        var declaration = await source.CreateDeclarationAsync();

        Assert.Equal(GroundworkDesignAtomicWriteStorageManifest.FeatureIdentity, source.FeatureIdentity);
        Assert.Empty(declaration.RequiredStoreContracts);
        var route = Assert.Single(declaration.RequiredRoutes);
        Assert.Equal(
            GroundworkDesignAtomicWriteStorageManifest.DesignOperationDocumentKind,
            route.StorageUnit.Value);
        Assert.Equal(
            GroundworkDesignAtomicWriteStorageManifest.AtomicWriteRouteIdentity,
            route.RouteIdentity);
        Assert.Equal([WellKnownCapabilities.AtomicCommit], route.RequiredCapabilities);
        Assert.Equal(
            GroundworkDesignAtomicWriteStorageManifest.MultiDocumentTransactionsTopologyIdentity,
            Assert.Single(declaration.TopologyRequirements).Identity);
        Assert.Equal(
            GroundworkDesignAtomicWriteStorageManifest.AtomicWriteRouteIdentity,
            Assert.Single(declaration.CoverageRows));
    }
}
