using Groundwork.Kernel;
using Xunit;

namespace Elsa.Activities.Design.Persistence.Groundwork.Tests;

public sealed class ActivitiesDesignStorageManifestTests
{
    [Fact]
    public void Fresh_catalog_preserves_all_activity_design_units_as_scoped_optimistic_rows()
    {
        var units = ActivitiesDesignStorageManifest.CreateUnits();

        Assert.Equal(21, units.Count);
        Assert.Equal(units.Count, units.Select(unit => unit.Id.Value).Distinct(StringComparer.Ordinal).Count());
        Assert.All(units, unit =>
        {
            Assert.Equal(ScopePolicy.Scoped, unit.Scope);
            Assert.True(unit.Concurrency.IsOptimistic);
            Assert.Equal(ActivitiesDesignStorageManifest.StorageSchemaVersion, unit.SchemaVersion);
            Assert.Equal([ActivitiesDesignStorageManifest.IdField], unit.Key.Columns);
            Assert.Equal(
                PortableType.String,
                unit.Columns.Single(column => column.Name == ActivitiesDesignStorageManifest.IdField).Type);
            Assert.Equal(
                PortableType.String,
                unit.Columns.Single(column => column.Name == ActivitiesDesignStorageManifest.SchemaVersionField).Type);
            Assert.Equal(
                PortableType.Json,
                unit.Columns.Single(column => column.Name == ActivitiesDesignStorageManifest.ContentField).Type);
            Assert.Equal(
                PortableType.Int64,
                unit.Columns.Single(column => column.Name == ActivitiesDesignStorageManifest.RevisionField).Type);
        });
    }

    [Fact]
    public void Activity_design_units_compile_to_stable_physical_names()
    {
        var units = ActivitiesDesignStorageManifest.CreateUnits().ToDictionary(unit => unit.Id.Value);

        Assert.Equal("elsa_activity_definitions", units[ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind].Name);
        Assert.Equal("elsa_activity_definition_versions", units[ActivitiesDesignStorageManifest.ActivityDefinitionVersionDocumentKind].Name);
        Assert.Equal("elsa_activity_management_definitions", units[ActivitiesDesignStorageManifest.ActivityDefinitionManagementProjectionDocumentKind].Name);
        Assert.Equal("elsa_activity_design_operations", units[ActivitiesDesignStorageManifest.DesignOperationDocumentKind].Name);
    }

    [Fact]
    public void Reusable_activity_routes_preserve_the_current_index_evidence()
    {
        var units = ActivitiesDesignStorageManifest.CreateUnits().ToDictionary(unit => unit.Id.Value);

        AssertIndex(
            units[ActivitiesDesignStorageManifest.ActivityDefinitionAuthoringStateDocumentKind],
            ActivitiesDesignStorageManifest.ByDefinitionIndex,
            ActivitiesDesignStorageManifest.DefinitionIdField,
            ActivitiesDesignStorageManifest.EntityIdField);
        AssertIndex(
            units[ActivitiesDesignStorageManifest.ActivityDefinitionAuthoringStateDocumentKind],
            ActivitiesDesignStorageManifest.ByHeadVersionIndex,
            ActivitiesDesignStorageManifest.HeadVersionIdField,
            ActivitiesDesignStorageManifest.EntityIdField);
        AssertIndex(
            units[ActivitiesDesignStorageManifest.ActivityDefinitionDraftDocumentKind],
            ActivitiesDesignStorageManifest.ByDefinitionIndex,
            ActivitiesDesignStorageManifest.DefinitionIdField,
            ActivitiesDesignStorageManifest.EntityIdField);
        AssertIndex(
            units[ActivitiesDesignStorageManifest.ActivityDefinitionDraftLayoutDocumentKind],
            ActivitiesDesignStorageManifest.ByDraftIndex,
            ActivitiesDesignStorageManifest.DraftIdField,
            ActivitiesDesignStorageManifest.EntityIdField);
        AssertIndex(
            units[ActivitiesDesignStorageManifest.ActivityDraftValidationDocumentKind],
            ActivitiesDesignStorageManifest.ByDraftIndex,
            ActivitiesDesignStorageManifest.DraftIdField,
            ActivitiesDesignStorageManifest.EntityIdField);
        AssertIndex(
            units[ActivitiesDesignStorageManifest.ActivityDefinitionVersionPublicationDocumentKind],
            ActivitiesDesignStorageManifest.ByDefinitionIndex,
            ActivitiesDesignStorageManifest.DefinitionIdField,
            ActivitiesDesignStorageManifest.EntityIdField);
        AssertIndex(
            units[ActivitiesDesignStorageManifest.ActivityDefinitionVersionPublicationDocumentKind],
            ActivitiesDesignStorageManifest.ByDefinitionVersionIndex,
            ActivitiesDesignStorageManifest.DefinitionVersionIdField,
            ActivitiesDesignStorageManifest.EntityIdField);
        AssertIndex(
            units[ActivitiesDesignStorageManifest.ActivityDefinitionVersionLayoutDocumentKind],
            ActivitiesDesignStorageManifest.ByDefinitionVersionIndex,
            ActivitiesDesignStorageManifest.DefinitionVersionIdField,
            ActivitiesDesignStorageManifest.EntityIdField);
        AssertIndex(
            units[ActivitiesDesignStorageManifest.ActivityDependencyEdgeDocumentKind],
            ActivitiesDesignStorageManifest.ByOwnerVersionIndex,
            ActivitiesDesignStorageManifest.OwnerVersionIdField,
            ActivitiesDesignStorageManifest.EntityIdField);
        AssertIndex(
            units[ActivitiesDesignStorageManifest.ActivityDependencyEdgeDocumentKind],
            ActivitiesDesignStorageManifest.ByDependencyVersionIndex,
            ActivitiesDesignStorageManifest.DependencyVersionIdField,
            ActivitiesDesignStorageManifest.EntityIdField);
    }

    [Fact]
    public void No_activity_unit_declares_a_by_collection_enumeration_index()
    {
        foreach (var unit in ActivitiesDesignStorageManifest.CreateUnits())
            Assert.DoesNotContain(unit.Indexes, index => index.Name == "by_collection");
    }

    [Fact]
    public void Fork_receipt_is_append_only_in_the_clean_v2_unit_catalog()
    {
        var receipt = ActivitiesDesignStorageManifest.Require(
            ActivitiesDesignStorageManifest.ActivityForkReceiptDocumentKind);

        Assert.Equal([ActivitiesDesignStorageManifest.IdField], receipt.Key.Columns);
        Assert.True(receipt.Concurrency.IsOptimistic);
    }

    [Fact]
    public void Fork_candidate_retention_preserves_the_retention_and_identity_index_shape()
    {
        var candidate = ActivitiesDesignStorageManifest.Require(
            ActivitiesDesignStorageManifest.ActivityForkCandidateDocumentKind);
        AssertIndex(
            candidate,
            ActivitiesDesignStorageManifest.ActivityForkCandidateRetentionIndex,
            ActivitiesDesignStorageManifest.ActivityForkCandidateRetentionField,
            ActivitiesDesignStorageManifest.EntityIdField);
    }

    [Fact]
    public void Schema_version_remains_the_frozen_activity_design_stamp()
    {
        Assert.Equal("1.0.0", ActivitiesDesignStorageManifest.SchemaVersion);
    }

    private static void AssertIndex(StorageUnit unit, string name, params string[] columns)
    {
        var index = Assert.Single(unit.Indexes, candidate => candidate.Name == name);
        Assert.Equal(columns, index.Columns.Select(column => column.Column));
    }
}
