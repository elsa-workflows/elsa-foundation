using Groundwork.Kernel;
using Xunit;

namespace Elsa.Activities.Design.Persistence.Groundwork.Tests;

public sealed class ActivitiesDesignStorageManifestTests
{
    [Fact]
    public void Manifest_compiles_all_activity_design_units_to_scoped_physical_entity_tables()
    {
        var units = ActivitiesDesignStorageManifest.CreateUnits();

        Assert.Equal(21, units.Count);
        Assert.Equal(units.Count, units.Select(unit => unit.Id.Value).Distinct(StringComparer.Ordinal).Count());
        Assert.All(units, unit =>
        {
            Assert.Equal(ScopePolicy.Scoped, unit.Scope);
            Assert.True(unit.Concurrency.IsOptimistic);
            Assert.Equal(
                unit.Id.Value == ActivitiesDesignStorageManifest.ActivityDefinitionVersionDocumentKind
                    ? ActivitiesDesignStorageManifest.ActivityDefinitionVersionStorageSchemaVersion
                    : ActivitiesDesignStorageManifest.StorageSchemaVersion,
                unit.SchemaVersion);
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
        Assert.Equal("elsa_activity_definition_versions_v2", units[ActivitiesDesignStorageManifest.ActivityDefinitionVersionDocumentKind].Name);
        Assert.Equal("elsa_activity_management_definitions", units[ActivitiesDesignStorageManifest.ActivityDefinitionManagementProjectionDocumentKind].Name);
        Assert.Equal("elsa_activity_design_operations", units[ActivitiesDesignStorageManifest.DesignOperationDocumentKind].Name);
    }

    [Fact]
    public void Reusable_activity_routes_preserve_the_current_query_identities_and_physical_index_evidence()
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
    public void Version_routes_share_one_bounded_composite_index_for_both_public_query_shapes()
    {
        var unit = ActivitiesDesignStorageManifest.Require(
            ActivitiesDesignStorageManifest.ActivityDefinitionVersionDocumentKind);
        Assert.Equal(
            128,
            unit.Columns.Single(column => column.Name == ActivitiesDesignStorageManifest.ActivityDefinitionVersionSemVerSortKeyField).MaxLength);
        Assert.False(unit.Columns.Single(column =>
            column.Name == ActivitiesDesignStorageManifest.ActivityDefinitionVersionDefinitionIdField).IsNullable);
        Assert.False(unit.Columns.Single(column =>
            column.Name == ActivitiesDesignStorageManifest.ActivityDefinitionVersionSemVerSortKeyField).IsNullable);

        var versionIndex = AssertIndex(
            unit,
            ActivitiesDesignStorageManifest.ActivityDefinitionVersionByDefinitionIndex,
            ActivitiesDesignStorageManifest.ActivityDefinitionVersionDefinitionIdField,
            ActivitiesDesignStorageManifest.ActivityDefinitionVersionSemVerSortKeyField);
        Assert.True(versionIndex.IsUnique);
        Assert.Equal(
            ActivitiesDesignStorageManifest.ActivityDefinitionVersionByDefinitionIndex,
            ActivitiesDesignStorageManifest.ActivityDefinitionVersionByDefinitionAndSortKeyIndex);
        Assert.Equal(
            [ActivitiesDesignStorageManifest.ActivityDefinitionVersionDefinitionIdField,
                ActivitiesDesignStorageManifest.ActivityDefinitionVersionSemVerSortKeyField],
            ActivitiesDesignStorageManifest.ActivityDefinitionVersionOrder.Select(order => order.Field));
        Assert.Single(
            unit.Indexes,
            index => index.Name == ActivitiesDesignStorageManifest.ActivityDefinitionVersionByDefinitionIndex);
    }

    [Fact]
    public void Version_composite_index_keys_fit_the_sql_server_nonclustered_budget()
    {
        const int sqlServerMaxNonclusteredIndexKeyBytes = 1_700;
        const int sqlServerUnicodeBytesPerCodeUnit = 2;
        const int groundworkScopeMaximumLength = 128;
        var unit = ActivitiesDesignStorageManifest.Require(
            ActivitiesDesignStorageManifest.ActivityDefinitionVersionDocumentKind);

        foreach (var index in unit.Indexes.Where(index =>
                     index.Name is ActivitiesDesignStorageManifest.ActivityDefinitionVersionByDefinitionIndex))
        {
            var declaredKeyBytes = index.Columns
                .Select(column => unit.Columns.Single(candidate => candidate.Name == column.Column))
                .Where(column => column.Type == PortableType.String)
                .Sum(column => column.MaxLength * sqlServerUnicodeBytesPerCodeUnit);
            var keyBytes = declaredKeyBytes + groundworkScopeMaximumLength * sqlServerUnicodeBytesPerCodeUnit;
            Assert.True(
                keyBytes <= sqlServerMaxNonclusteredIndexKeyBytes,
                $"Index '{index.Name}' requires {keyBytes} bytes including Groundwork's scoped key, over SQL Server's {sqlServerMaxNonclusteredIndexKeyBytes}-byte limit.");
        }

        var definitionUnit = ActivitiesDesignStorageManifest.Require(
            ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind);
        Assert.Equal(
            ActivitiesDesignStorageManifest.ManagementSearchMaximumLength,
            definitionUnit.Columns.Single(column => column.Name == ActivitiesDesignStorageManifest.ManagementSearchField).MaxLength);
    }

    [Fact]
    public void Definition_named_routes_have_one_matching_deterministic_index_each()
    {
        var unit = ActivitiesDesignStorageManifest.Require(
            ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind);

        AssertIndex(unit, "activity_definition_by_type_key",
            ActivitiesDesignStorageManifest.ActivityDefinitionTypeKeyField,
            ActivitiesDesignStorageManifest.EntityIdField);
        AssertIndex(unit, "activity_definition_by_category",
            ActivitiesDesignStorageManifest.ActivityDefinitionCategoryField,
            ActivitiesDesignStorageManifest.EntityIdField);
        AssertIndex(unit, "activity_definition_by_display_name",
            ActivitiesDesignStorageManifest.ActivityDefinitionDisplayNameField,
            ActivitiesDesignStorageManifest.EntityIdField);
        AssertIndex(unit, "activity_definition_by_description",
            ActivitiesDesignStorageManifest.ActivityDefinitionDescriptionField,
            ActivitiesDesignStorageManifest.EntityIdField);
        Assert.Equal(
            [ActivitiesDesignStorageManifest.ActivityDefinitionTypeKeyField,
                ActivitiesDesignStorageManifest.EntityIdField],
            ActivitiesDesignStorageManifest.ActivityDefinitionTypeKeyOrder.Select(order => order.Field));
        Assert.Equal(
            [ActivitiesDesignStorageManifest.ActivityDefinitionCategoryField,
                ActivitiesDesignStorageManifest.EntityIdField],
            ActivitiesDesignStorageManifest.ActivityDefinitionCategoryOrder.Select(order => order.Field));
        Assert.Equal(
            [ActivitiesDesignStorageManifest.ActivityDefinitionDisplayNameField,
                ActivitiesDesignStorageManifest.EntityIdField],
            ActivitiesDesignStorageManifest.ActivityDefinitionDisplayNameOrder.Select(order => order.Field));
        Assert.Equal(
            [ActivitiesDesignStorageManifest.ActivityDefinitionDescriptionField,
                ActivitiesDesignStorageManifest.EntityIdField],
            ActivitiesDesignStorageManifest.ActivityDefinitionDescriptionOrder.Select(order => order.Field));
    }

    [Fact]
    public void Management_projection_units_declare_current_page_and_retention_shapes()
    {
        var units = ActivitiesDesignStorageManifest.CreateUnits()
            .Where(unit => unit.Id.Value is
                ActivitiesDesignStorageManifest.ActivityDefinitionManagementProjectionDocumentKind or
                ActivitiesDesignStorageManifest.ActivityDraftManagementProjectionDocumentKind or
                ActivitiesDesignStorageManifest.ActivityVersionManagementProjectionDocumentKind)
            .ToArray();

        Assert.Equal(3, units.Length);
        foreach (var unit in units)
        {
            Assert.Contains(unit.Indexes, index => index.Name == ActivitiesDesignStorageManifest.ManagementExpiredIndex);
            Assert.Contains(unit.Indexes, index => index.Name.EndsWith("_identity_asc", StringComparison.Ordinal));
            Assert.Contains(unit.Columns, column => column.Name == ActivitiesDesignStorageManifest.ManagementValidFromField);
            Assert.Contains(unit.Columns, column => column.Name == ActivitiesDesignStorageManifest.ManagementValidToField);
            Assert.Contains(unit.Columns, column => column.Name == ActivitiesDesignStorageManifest.ManagementVisibilityField);
            Assert.Contains(unit.Columns, column => column.Name == ActivitiesDesignStorageManifest.ManagementSearchField);
        }
    }

    [Fact]
    public void Projection_widths_match_the_declared_query_contract()
    {
        var definition = ActivitiesDesignStorageManifest.Require(
            ActivitiesDesignStorageManifest.ActivityDefinitionDocumentKind);
        var search = definition.Columns.Single(column => column.Name == ActivitiesDesignStorageManifest.ManagementSearchField);
        var typeKey = definition.Columns.Single(column => column.Name == ActivitiesDesignStorageManifest.ActivityDefinitionTypeKeyField);
        var id = definition.Columns.Single(column => column.Name == ActivitiesDesignStorageManifest.IdField);

        Assert.Equal(ActivitiesDesignStorageManifest.ManagementSearchMaximumLength, search.MaxLength);
        Assert.Equal(256, typeKey.MaxLength);
        Assert.Equal(ActivitiesDesignStorageManifest.MaximumIdLength, id.MaxLength);
        Assert.True(search.MaxLength > typeKey.MaxLength);
        Assert.True(id.MaxLength > typeKey.MaxLength);
    }

    [Fact]
    public void No_activity_unit_declares_a_by_collection_enumeration_route()
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
    public void Fork_candidate_retention_preserves_the_preview95_index_shape_and_routes_to_v2()
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
    public void Schema_version_remains_the_frozen_legacy_stamp()
    {
        Assert.Equal("1.0.0", ActivitiesDesignStorageManifest.SchemaVersion);
    }

    [Fact]
    public void Authoring_state_list_by_definition_route_admits_equality_and_in()
    {
        var unit = ActivitiesDesignStorageManifest.Require(
            ActivitiesDesignStorageManifest.ActivityDefinitionAuthoringStateDocumentKind);
        var index = Assert.Single(unit.Indexes, candidate => candidate.Name == ActivitiesDesignStorageManifest.ByDefinitionIndex);
        Assert.Equal(
            [ActivitiesDesignStorageManifest.DefinitionIdField, ActivitiesDesignStorageManifest.EntityIdField],
            index.Columns.Select(column => column.Column));
    }

    [Fact]
    public void Offset_routes_use_unique_bounded_identity_tuples_instead_of_the_wide_provider_comparison_key()
    {
        foreach (var unit in ActivitiesDesignStorageManifest.CreateUnits())
        {
            foreach (var index in unit.Indexes)
            {
                Assert.DoesNotContain("comparisonKey", index.Columns.Select(column => column.Column));

                // The version index intentionally serves both the ordered list and exact
                // (definition, sort-key) lookup.  semVerSortKey is unique within a definition,
                // so the exact lookup does not need the entity id in its physical key.
                if (index.Name is not (ActivitiesDesignStorageManifest.ActivityDefinitionVersionByDefinitionIndex
                    or ActivitiesDesignStorageManifest.ManagementExpiredIndex))
                    Assert.Contains(ActivitiesDesignStorageManifest.EntityIdField,
                        index.Columns.Select(column => column.Column));
            }
        }
    }

    private static IndexDefinition AssertIndex(StorageUnit unit, string name, params string[] columns)
    {
        var index = Assert.Single(unit.Indexes, candidate => candidate.Name == name);
        Assert.Equal(columns, index.Columns.Select(column => column.Column));
        return index;
    }
}
