using Groundwork.Core.Indexing;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Queries;
using Xunit;

namespace Elsa.Activities.Design.Persistence.Groundwork.Tests;

public sealed class ActivitiesDesignStorageManifestTests
{
    [Fact]
    public void Manifest_compiles_all_activity_design_units_to_scoped_physical_entity_tables()
    {
        var manifest = ActivitiesDesignStorageManifest.Create();
        var resolution = PhysicalStorageResolver.Resolve(
            manifest,
            PhysicalNamePolicy.Identity,
            ProviderPhysicalNameNormalizer.Identity);

        Assert.True(
            resolution.IsValid,
            string.Join(Environment.NewLine, resolution.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Equal(manifest.StorageUnits.Count, resolution.Definitions.Count);
        Assert.All(manifest.StorageUnits, unit =>
        {
            var storage = Assert.IsType<StorageUnitPhysicalStorage>(unit.PhysicalStorage);
            var table = Assert.IsType<PhysicalStoragePolicy.ExplicitPolicy>(storage.Policy).Definition;

            Assert.Equal(StorageUnitProvisioningMode.Declared, storage.ProvisioningMode);
            Assert.Equal(PhysicalStorageForm.PhysicalEntityTable, table.Form);
        });
    }

    [Fact]
    public void Reusable_activity_routes_preserve_the_current_query_identities_and_physical_index_evidence()
    {
        var manifest = ActivitiesDesignStorageManifest.Create();

        AssertUnit(
            manifest,
            ActivitiesDesignStorageManifest.ActivityDefinitionAuthoringStateDocumentKind,
            (ActivitiesDesignStorageManifest.ByDefinitionIndex, "list-by-definition", ActivitiesDesignStorageManifest.DefinitionIdField),
            (ActivitiesDesignStorageManifest.ByHeadVersionIndex, "list-by-head-version", ActivitiesDesignStorageManifest.HeadVersionIdField));
        AssertUnit(
            manifest,
            ActivitiesDesignStorageManifest.ActivityDefinitionDraftDocumentKind,
            (ActivitiesDesignStorageManifest.ByDefinitionIndex, "list-by-definition", ActivitiesDesignStorageManifest.DefinitionIdField));
        AssertUnit(
            manifest,
            ActivitiesDesignStorageManifest.ActivityDefinitionDraftLayoutDocumentKind,
            (ActivitiesDesignStorageManifest.ByDraftIndex, "list-by-draft", ActivitiesDesignStorageManifest.DraftIdField));
        AssertUnit(
            manifest,
            ActivitiesDesignStorageManifest.ActivityDraftValidationDocumentKind,
            (ActivitiesDesignStorageManifest.ByDraftIndex, "list-by-draft", ActivitiesDesignStorageManifest.DraftIdField));
        AssertUnit(
            manifest,
            ActivitiesDesignStorageManifest.ActivityDefinitionVersionPublicationDocumentKind,
            (ActivitiesDesignStorageManifest.ByDefinitionIndex, "list-by-definition", ActivitiesDesignStorageManifest.DefinitionIdField),
            (ActivitiesDesignStorageManifest.ByDefinitionVersionIndex, "list-by-definition-version", ActivitiesDesignStorageManifest.DefinitionVersionIdField));
        AssertUnit(
            manifest,
            ActivitiesDesignStorageManifest.ActivityDefinitionVersionLayoutDocumentKind,
            (ActivitiesDesignStorageManifest.ByDefinitionVersionIndex, "list-by-definition-version", ActivitiesDesignStorageManifest.DefinitionVersionIdField));
        AssertUnit(
            manifest,
            ActivitiesDesignStorageManifest.ActivityDependencyEdgeDocumentKind,
            (ActivitiesDesignStorageManifest.ByOwnerVersionIndex, "list-by-owner-version", ActivitiesDesignStorageManifest.OwnerVersionIdField),
            (ActivitiesDesignStorageManifest.ByDependencyVersionIndex, "list-by-dependency-version", ActivitiesDesignStorageManifest.DependencyVersionIdField));
    }

    [Fact]
    public void No_activity_unit_declares_a_by_collection_enumeration_route()
    {
        foreach (var unit in ActivitiesDesignStorageManifest.Create().StorageUnits)
        {
            var storage = Assert.IsType<StorageUnitPhysicalStorage>(unit.PhysicalStorage);
            var table = Assert.IsType<PhysicalStoragePolicy.ExplicitPolicy>(storage.Policy).Definition;

            Assert.DoesNotContain(storage.LogicalIndexes, index => index.Identity == "by-collection");
            Assert.DoesNotContain(table.Indexes, index => index.LogicalName == "by-collection");
            Assert.All(storage.BoundedQueries, query => Assert.NotEqual("list-all", query.Identity));
        }
    }

    [Fact]
    public void Authoring_state_list_by_definition_route_admits_equality_and_in()
    {
        var manifest = ActivitiesDesignStorageManifest.Create();
        var unit = manifest.StorageUnits.Single(unit =>
            unit.Identity.Value == ActivitiesDesignStorageManifest.ActivityDefinitionAuthoringStateDocumentKind);
        var storage = Assert.IsType<StorageUnitPhysicalStorage>(unit.PhysicalStorage);
        var query = Assert.Single(storage.BoundedQueries, candidate => candidate.Identity == "list-by-definition");

        Assert.Contains(query.PredicateFields, field =>
            field.Path == ActivitiesDesignStorageManifest.DefinitionIdField &&
            field.Operations.Contains(PortableQueryOperation.Equal) &&
            field.Operations.Contains(PortableQueryOperation.In));
        var table = Assert.IsType<PhysicalStoragePolicy.ExplicitPolicy>(storage.Policy).Definition;
        var definitionId = Assert.Single(table.ProjectedColumns, column =>
            column.Path == ActivitiesDesignStorageManifest.DefinitionIdField);
        Assert.False(definitionId.IsNullable);
    }

    [Theory]
    [InlineData(
        ActivitiesDesignStorageManifest.ActivityUpgradePlanDocumentKind,
        ActivitiesDesignStorageManifest.ActivityUpgradePlanIdField)]
    [InlineData(
        ActivitiesDesignStorageManifest.ActivityUpgradeApplyReceiptDocumentKind,
        ActivitiesDesignStorageManifest.ActivityUpgradeApplyReceiptIdField)]
    public void Upgrade_documents_project_their_actual_persisted_identity(
        string documentKind,
        string identityPath)
    {
        var unit = ActivitiesDesignStorageManifest.Create().StorageUnits.Single(candidate =>
            candidate.Identity.Value == documentKind);
        var storage = Assert.IsType<StorageUnitPhysicalStorage>(unit.PhysicalStorage);
        var table = Assert.IsType<PhysicalStoragePolicy.ExplicitPolicy>(storage.Policy).Definition;
        var identity = Assert.Single(table.ProjectedColumns, column => column.LogicalName == "entity_id");

        Assert.Equal(identityPath, identity.Path);
        Assert.False(identity.IsNullable);
    }

    [Fact]
    public void Schema_version_remains_the_frozen_legacy_stamp()
    {
        Assert.Equal("1.0.0", ActivitiesDesignStorageManifest.SchemaVersion);
    }

    [Fact]
    public void Offset_routes_use_unique_bounded_identity_tuples_instead_of_the_wide_provider_comparison_key()
    {
        var comparisonKey = new DocumentEnvelopeDefinition().IdComparisonKeyColumn;
        foreach (var unit in ActivitiesDesignStorageManifest.Create().StorageUnits)
        {
            var storage = Assert.IsType<StorageUnitPhysicalStorage>(unit.PhysicalStorage);
            var table = Assert.IsType<PhysicalStoragePolicy.ExplicitPolicy>(storage.Policy).Definition;
            foreach (var route in storage.BoundedQueries.Where(route =>
                         route.PagingSupport == QueryPagingSupport.Offset))
            {
                Assert.True(storage.LogicalIndexes.Single(index => index.Identity == route.IndexIdentity).IsUnique);
                var physical = table.Indexes.Single(index => index.LogicalName == route.IndexIdentity);
                Assert.True(physical.IsUnique);
                Assert.DoesNotContain(physical.Columns, column =>
                    column.ColumnLogicalName == comparisonKey);
            }
        }
    }

    [Fact]
    public void Fork_candidate_retention_preserves_the_preview95_index_shape_and_routes_to_v2()
    {
        var unit = ActivitiesDesignStorageManifest.Create().StorageUnits.Single(x =>
            x.Identity.Value == ActivitiesDesignStorageManifest.ActivityForkCandidateDocumentKind);
        var storage = Assert.IsType<StorageUnitPhysicalStorage>(unit.PhysicalStorage);
        var table = Assert.IsType<PhysicalStoragePolicy.ExplicitPolicy>(storage.Policy).Definition;
        var legacyIdentity = ActivitiesDesignStorageManifest.ActivityForkCandidateRetentionIndex;
        var v2Identity = $"{legacyIdentity}-v2";
        var legacyIndex = Assert.Single(storage.LogicalIndexes, index => index.Identity == legacyIdentity);
        var v2Index = Assert.Single(storage.LogicalIndexes, index => index.Identity == v2Identity);
        var legacyPhysicalIndex = Assert.Single(table.Indexes, index => index.LogicalName == legacyIdentity);
        var v2PhysicalIndex = Assert.Single(table.Indexes, index => index.LogicalName == v2Identity);
        var query = Assert.Single(storage.BoundedQueries, candidate =>
            candidate.Identity == ActivitiesDesignStorageManifest.ActivityForkCandidateExpiredQuery);

        Assert.Equal(
            [ActivitiesDesignStorageManifest.ActivityForkCandidateRetentionField],
            legacyIndex.Fields.Select(field => field.Path));
        Assert.Equal(
            [
                ActivitiesDesignStorageManifest.ActivityForkCandidateRetentionField,
                ActivitiesDesignStorageManifest.EntityIdField
            ],
            v2Index.Fields.Select(field => field.Path));
        Assert.DoesNotContain(legacyPhysicalIndex.Columns, column => column.ColumnLogicalName == "entity_id");
        Assert.Equal("entity_id", v2PhysicalIndex.Columns.Last().ColumnLogicalName);
        Assert.Equal(v2Identity, query.IndexIdentity);
    }

    private static void AssertUnit(
        global::Groundwork.Core.Manifests.StorageManifest manifest,
        string documentKind,
        params (string Index, string Query, string Path)[] expectedRoutes)
    {
        var unit = manifest.StorageUnits.Single(unit => unit.Identity.Value == documentKind);
        var storage = Assert.IsType<StorageUnitPhysicalStorage>(unit.PhysicalStorage);
        var table = Assert.IsType<PhysicalStoragePolicy.ExplicitPolicy>(storage.Policy).Definition;

        foreach (var (indexIdentity, queryIdentity, path) in expectedRoutes)
        {
            var versionedIndexIdentity = $"{indexIdentity}-v2";
            var legacyIndex = Assert.Single(storage.LogicalIndexes, index => index.Identity == indexIdentity);
            var index = Assert.Single(storage.LogicalIndexes, index => index.Identity == versionedIndexIdentity);
            Assert.Equal([path, ActivitiesDesignStorageManifest.EntityIdField], legacyIndex.Fields.Select(field => field.Path));
            Assert.Equal([path, ActivitiesDesignStorageManifest.EntityIdField], index.Fields.Select(field => field.Path));
            var query = Assert.Single(storage.BoundedQueries, query =>
                query.Identity == queryIdentity && query.IndexIdentity == versionedIndexIdentity);
            Assert.Equal(
                [
                    new BoundedQuerySortField(path, PhysicalSortDirection.Ascending),
                    new BoundedQuerySortField(ActivitiesDesignStorageManifest.EntityIdField, PhysicalSortDirection.Ascending)
                ],
                query.SortFields);
            var legacyPhysicalIndex = Assert.Single(table.Indexes, index => index.LogicalName == indexIdentity);
            var physicalIndex = Assert.Single(table.Indexes, index => index.LogicalName == versionedIndexIdentity);
            Assert.False(legacyIndex.IsUnique);
            Assert.False(legacyPhysicalIndex.IsUnique);
            Assert.True(index.IsUnique);
            Assert.True(physicalIndex.IsUnique);
            Assert.Equal("entity_id", legacyPhysicalIndex.Columns.Last().ColumnLogicalName);
            Assert.Equal("entity_id", physicalIndex.Columns.Last().ColumnLogicalName);
        }
    }
}
