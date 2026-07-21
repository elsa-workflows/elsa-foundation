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
            Assert.Empty(unit.Indexes);
            Assert.Empty(unit.Queries);
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
    }

    [Fact]
    public void Schema_version_remains_the_frozen_legacy_stamp()
    {
        Assert.Equal("1.0.0", ActivitiesDesignStorageManifest.SchemaVersion);
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
            var index = Assert.Single(storage.LogicalIndexes, index => index.Identity == indexIdentity);
            Assert.Equal([path, ActivitiesDesignStorageManifest.DocumentIdField], index.Fields.Select(field => field.Path));
            var query = Assert.Single(storage.BoundedQueries, query =>
                query.Identity == queryIdentity && query.IndexIdentity == indexIdentity);
            Assert.Equal(
                [
                    new BoundedQuerySortField(path, PhysicalSortDirection.Ascending),
                    new BoundedQuerySortField(ActivitiesDesignStorageManifest.DocumentIdField, PhysicalSortDirection.Ascending)
                ],
                query.SortFields);
            var physicalIndex = Assert.Single(table.Indexes, index => index.LogicalName == indexIdentity);
            Assert.False(index.IsUnique);
            Assert.False(physicalIndex.IsUnique);
            Assert.Equal("id_comparison_key", physicalIndex.Columns.Last().ColumnLogicalName);
        }
    }
}
