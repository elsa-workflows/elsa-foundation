using Groundwork.Core.Capabilities;
using Groundwork.Core.Queries;
using Groundwork.Core.Manifests;
using Groundwork.Core.PhysicalStorage;
using Xunit;

namespace Elsa.Persistence.Groundwork.Querying.Tests;

public sealed class GroundworkDesignAtomicWriteStorageManifestTests
{
    [Fact]
    public void Manifest_owns_a_scoped_dedicated_operation_table()
    {
        var manifest = GroundworkDesignAtomicWriteStorageManifest.Create();

        Assert.Equal(GroundworkDesignAtomicWriteStorageManifest.FeatureIdentity, manifest.Identity.Value);
        Assert.Equal(GroundworkDesignAtomicWriteStorageManifest.ManifestOwner, manifest.Owner.Value);
        Assert.Equal(GroundworkDesignAtomicWriteStorageManifest.SchemaVersion, manifest.Version.Value);
        var unit = Assert.Single(
            manifest.StorageUnits,
            candidate => candidate.Identity.Value ==
                GroundworkDesignAtomicWriteStorageManifest.DesignOperationDocumentKind);
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
        // The ledger stays route-free: it is written and read by id within the design commit, never swept.
        Assert.Empty(storage.LogicalIndexes);
        Assert.Empty(storage.BoundedQueries);
    }

    /// <summary>
    /// The intent outbox is the ledger's opposite in the ways that matter: mutable because an intent is
    /// deleted once delivered, and route-bearing because a redrive has to find claimable work. Its projected
    /// id is length-bound because SQL Server refuses an unbounded string as an index key column, while
    /// SQLite and PostgreSQL accept one — so this assertion is the cheap standing guard for a constraint
    /// only one of the four providers enforces.
    /// </summary>
    [Fact]
    public void Manifest_owns_a_mutable_intent_outbox_with_a_bounded_claim_route()
    {
        var manifest = GroundworkDesignAtomicWriteStorageManifest.Create();

        var unit = Assert.Single(
            manifest.StorageUnits,
            candidate => candidate.Identity.Value ==
                GroundworkDesignAtomicWriteStorageManifest.DesignPostCommitIntentDocumentKind);
        Assert.Equal(StorageLifecycleKind.Mutable, unit.Lifecycle.Kind);
        Assert.Equal(TenancyKind.Scoped, unit.Tenancy.Kind);
        Assert.Equal(ConcurrencyKind.Optimistic, unit.Concurrency.Kind);

        var storage = unit.PhysicalStorage!;
        var policy = Assert.IsType<PhysicalStoragePolicy.ExplicitPolicy>(storage.Policy);
        Assert.Equal(PhysicalStorageForm.DedicatedDocumentTable, policy.Definition.Form);

        var route = Assert.Single(storage.BoundedQueries);
        Assert.Equal(
            GroundworkDesignAtomicWriteStorageManifest.ListClaimableDesignPostCommitIntentsQuery,
            route.Identity);
        // Offset, never cursor: a claim mutates claimableAt, so the ordering key moves under a cursor.
        Assert.Equal(QueryPagingSupport.Offset, route.PagingSupport);

        var id = Assert.Single(
            policy.Definition.ProjectedColumns,
            column => column.LogicalName ==
                GroundworkDesignAtomicWriteStorageManifest.DesignPostCommitIntentIdField);
        Assert.Equal(
            GroundworkDesignAtomicWriteStorageManifest.DesignPostCommitIntentIdProjectionLength,
            id.Length);
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
