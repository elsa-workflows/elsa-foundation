using Groundwork.Kernel;
using Xunit;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Tests;

public sealed class WorkflowsDesignStorageManifestTests
{
    [Fact]
    public void Manifest_compiles_all_workflow_design_routes_to_scoped_physical_entity_tables()
    {
        var units = WorkflowsDesignStorageManifest.CreateUnits();

        Assert.Equal(
            ["workflowDefinition", "workflowDefinitionDraft", "workflowDefinitionVersion", "workflowDefinitionVersionLayout", "workflowDesignOperation"],
            units.Select(unit => unit.Id.Value).Order(StringComparer.Ordinal));
        Assert.Equal(5, units.Count);
        Assert.Equal(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [WorkflowsDesignStorageManifest.DesignOperationDocumentKind] = "elsa_design_operations",
                [WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind] = "elsa_workflow_definitions",
                [WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind] = "elsa_workflow_definition_drafts",
                [WorkflowsDesignStorageManifest.WorkflowDefinitionVersionDocumentKind] = "elsa_workflow_definition_versions",
                [WorkflowsDesignStorageManifest.WorkflowDefinitionVersionLayoutDocumentKind] = "elsa_workflow_definition_version_layouts"
            },
            units.ToDictionary(unit => unit.Id.Value, unit => unit.Name, StringComparer.Ordinal));
        Assert.Equal(5, units.Select(unit => unit.Name).Distinct(StringComparer.Ordinal).Count());
        Assert.All(units, unit =>
        {
            Assert.Equal(ScopePolicy.Scoped, unit.Scope);
            Assert.True(unit.Concurrency.IsOptimistic);
            Assert.Equal([WorkflowsDesignStorageManifest.IdField], unit.Key.Columns);
            Assert.Contains(unit.Columns, column => column.Name == WorkflowsDesignStorageManifest.ContentField && column.Type == PortableType.Json);
            Assert.Contains(unit.Columns, column => column.Name == WorkflowsDesignStorageManifest.SchemaVersionField && column.IsNullable == false);
            Assert.Contains(unit.Columns, column => column.Name == WorkflowsDesignStorageManifest.TenantIdField);
            Assert.Equal(unit.Columns.Count, unit.Columns.Select(column => column.Name).Distinct(StringComparer.Ordinal).Count());
            Assert.Equal(unit.Indexes.Count, unit.Indexes.Select(index => index.Name).Distinct(StringComparer.Ordinal).Count());
        });
    }

    [Fact]
    public void Manifest_declares_only_bounded_physical_routes_with_complete_index_evidence()
    {
        var indexes = WorkflowsDesignStorageManifest.CreateUnits().SelectMany(unit => unit.Indexes).ToArray();
        var expected = new[]
        {
            WorkflowsDesignStorageManifest.DefinitionByIdIndex,
            WorkflowsDesignStorageManifest.DefinitionByIdSearchIndex,
            WorkflowsDesignStorageManifest.DefinitionByNameIndex,
            WorkflowsDesignStorageManifest.DefinitionByDescriptionIndex,
            WorkflowsDesignStorageManifest.VersionByDefinitionIndex,
            WorkflowsDesignStorageManifest.VersionByDefinitionAndSortKeyIndex,
            WorkflowsDesignStorageManifest.LatestVersionByDefinitionIndex,
            WorkflowsDesignStorageManifest.DraftByDefinitionIndex,
            WorkflowsDesignStorageManifest.LayoutByVersionIndex,
            WorkflowsDesignStorageManifest.OperationByKeyIndex
        };
        Assert.Equal(expected.Order(StringComparer.Ordinal), indexes.Select(index => index.Name).Order(StringComparer.Ordinal));
        Assert.All(indexes, index => Assert.NotEmpty(index.Columns));
        Assert.All(indexes, index => Assert.All(index.Columns, column => Assert.False(string.IsNullOrWhiteSpace(column.Column))));
        Assert.Equal(
            [
                WorkflowsDesignStorageManifest.FindDefinitionByIdQuery,
                WorkflowsDesignStorageManifest.ListDefinitionsByIdQuery,
                WorkflowsDesignStorageManifest.ListDefinitionsByNameQuery,
                WorkflowsDesignStorageManifest.ListDefinitionsByDescriptionQuery,
                WorkflowsDesignStorageManifest.SearchDefinitionsQuery,
                WorkflowsDesignStorageManifest.FindVersionByIdQuery,
                WorkflowsDesignStorageManifest.ListVersionsByDefinitionQuery,
                WorkflowsDesignStorageManifest.FindVersionByDefinitionAndSortKeyQuery,
                WorkflowsDesignStorageManifest.FindLatestVersionQuery,
                WorkflowsDesignStorageManifest.FindDraftByIdQuery,
                WorkflowsDesignStorageManifest.ListDraftsByDefinitionQuery,
                WorkflowsDesignStorageManifest.FindCurrentDraftByDefinitionQuery,
                WorkflowsDesignStorageManifest.FindLayoutByVersionQuery
            ],
            new[]
            {
                WorkflowsDesignStorageManifest.FindDefinitionByIdQuery,
                WorkflowsDesignStorageManifest.ListDefinitionsByIdQuery,
                WorkflowsDesignStorageManifest.ListDefinitionsByNameQuery,
                WorkflowsDesignStorageManifest.ListDefinitionsByDescriptionQuery,
                WorkflowsDesignStorageManifest.SearchDefinitionsQuery,
                WorkflowsDesignStorageManifest.FindVersionByIdQuery,
                WorkflowsDesignStorageManifest.ListVersionsByDefinitionQuery,
                WorkflowsDesignStorageManifest.FindVersionByDefinitionAndSortKeyQuery,
                WorkflowsDesignStorageManifest.FindLatestVersionQuery,
                WorkflowsDesignStorageManifest.FindDraftByIdQuery,
                WorkflowsDesignStorageManifest.ListDraftsByDefinitionQuery,
                WorkflowsDesignStorageManifest.FindCurrentDraftByDefinitionQuery,
                WorkflowsDesignStorageManifest.FindLayoutByVersionQuery
            });
    }

    [Fact]
    public void Version_and_draft_routes_have_deterministic_ordering_and_bounded_in_support()
    {
        var versions = WorkflowsDesignStorageManifest.Require(WorkflowsDesignStorageManifest.WorkflowDefinitionVersionDocumentKind);
        AssertIndex(versions, WorkflowsDesignStorageManifest.VersionByDefinitionIndex,
            [WorkflowsDesignStorageManifest.VersionDefinitionIdField, WorkflowsDesignStorageManifest.VersionSemVerSortKeyField, WorkflowsDesignStorageManifest.VersionIdField], unique: true);
        AssertIndex(versions, WorkflowsDesignStorageManifest.LatestVersionByDefinitionIndex,
            [WorkflowsDesignStorageManifest.VersionDefinitionIdField, WorkflowsDesignStorageManifest.VersionSemVerSortKeyField, WorkflowsDesignStorageManifest.VersionIdField], unique: false);
        var drafts = WorkflowsDesignStorageManifest.Require(WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind);
        AssertIndex(drafts, WorkflowsDesignStorageManifest.DraftByDefinitionIndex,
            [WorkflowsDesignStorageManifest.DraftDefinitionIdField, WorkflowsDesignStorageManifest.DraftLastModifiedAtField, WorkflowsDesignStorageManifest.DraftCreatedAtField, WorkflowsDesignStorageManifest.DraftIdField], unique: true);
        Assert.Equal(
            [
                WorkflowsDesignStorageManifest.VersionDefinitionIdField,
                WorkflowsDesignStorageManifest.VersionSemVerSortKeyField,
                WorkflowsDesignStorageManifest.VersionIdField
            ],
            versions.Indexes.Single(index => index.Name == WorkflowsDesignStorageManifest.VersionByDefinitionIndex).Columns.Select(column => column.Column));
        Assert.Equal(
            [
                WorkflowsDesignStorageManifest.DraftDefinitionIdField,
                WorkflowsDesignStorageManifest.DraftLastModifiedAtField,
                WorkflowsDesignStorageManifest.DraftCreatedAtField,
                WorkflowsDesignStorageManifest.DraftIdField
            ],
            drafts.Indexes.Single(index => index.Name == WorkflowsDesignStorageManifest.DraftByDefinitionIndex).Columns.Select(column => column.Column));
    }

    [Fact]
    public void Search_reuses_the_name_v2_index_without_a_duplicate_mongodb_key_shape()
    {
        var definitions = WorkflowsDesignStorageManifest.Require(WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind);
        Assert.Contains(definitions.Indexes, index => index.Name == WorkflowsDesignStorageManifest.DefinitionByNameIndex);
        Assert.DoesNotContain(definitions.Indexes, index => index.Name == "definition-by-search-v2");
        Assert.Equal(
            [WorkflowsDesignStorageManifest.DefinitionNameField, WorkflowsDesignStorageManifest.DefinitionIdField],
            definitions.Indexes.Single(index => index.Name == WorkflowsDesignStorageManifest.DefinitionByNameIndex).Columns.Select(column => column.Column));
        Assert.Equal(
            [WorkflowsDesignStorageManifest.DefinitionDescriptionField, WorkflowsDesignStorageManifest.DefinitionIdField],
            definitions.Indexes.Single(index => index.Name == WorkflowsDesignStorageManifest.DefinitionByDescriptionIndex).Columns.Select(column => column.Column));
        Assert.True(definitions.Columns.Single(column => column.Name == WorkflowsDesignStorageManifest.DefinitionIdField).IsNullable is false);
        Assert.True(definitions.Columns.Single(column => column.Name == WorkflowsDesignStorageManifest.DefinitionIdSearchKeyField).IsNullable is false);
        Assert.True(definitions.Indexes.Single(index => index.Name == WorkflowsDesignStorageManifest.DefinitionByIdIndex).IsUnique);
        Assert.Equal(
            [WorkflowsDesignStorageManifest.DefinitionIdSearchKeyField],
            definitions.Indexes.Single(index => index.Name == WorkflowsDesignStorageManifest.DefinitionByIdSearchIndex).Columns.Select(column => column.Column));
        Assert.True(definitions.Indexes.Single(index => index.Name == WorkflowsDesignStorageManifest.DefinitionByIdSearchIndex).IsUnique);
        Assert.True(definitions.Indexes.Single(index => index.Name == WorkflowsDesignStorageManifest.DefinitionByNameIndex).IsUnique);
        Assert.True(definitions.Indexes.Single(index => index.Name == WorkflowsDesignStorageManifest.DefinitionByDescriptionIndex).IsUnique);
        Assert.Equal(
            PortableCollation.UnicodeOrdinalIgnoreCase,
            definitions.Columns.Single(column => column.Name == WorkflowsDesignStorageManifest.DefinitionNameField).Collation);
        Assert.Equal(
            PortableCollation.UnicodeOrdinalIgnoreCase,
            definitions.Columns.Single(column => column.Name == WorkflowsDesignStorageManifest.DefinitionDescriptionField).Collation);
    }

    [Fact]
    public void Exact_version_route_enforces_uniqueness_on_definition_and_semver_sort_key_only()
    {
        var versions = WorkflowsDesignStorageManifest.Require(WorkflowsDesignStorageManifest.WorkflowDefinitionVersionDocumentKind);
        var index = versions.Indexes.Single(candidate => candidate.Name == WorkflowsDesignStorageManifest.VersionByDefinitionAndSortKeyIndex);
        Assert.True(index.IsUnique);
        Assert.Equal(
            [WorkflowsDesignStorageManifest.VersionDefinitionIdField, WorkflowsDesignStorageManifest.VersionSemVerSortKeyField],
            index.Columns.Select(column => column.Column));
        Assert.Equal(WorkflowsDesignStorageManifest.VersionDefinitionIdField, index.Columns[0].Column);
        Assert.Equal(WorkflowsDesignStorageManifest.VersionSemVerSortKeyField, index.Columns[1].Column);
        Assert.True(versions.Indexes.Single(candidate => candidate.Name == WorkflowsDesignStorageManifest.VersionByDefinitionIndex).IsUnique);
        Assert.False(versions.Indexes.Single(candidate => candidate.Name == WorkflowsDesignStorageManifest.LatestVersionByDefinitionIndex).IsUnique);
    }

    [Fact]
    public void Offset_routes_use_unique_entity_identity_tuples_without_the_wide_provider_comparison_key()
    {
        foreach (var unit in WorkflowsDesignStorageManifest.CreateUnits())
        {
            Assert.DoesNotContain(unit.Indexes, index => index.Columns.Any(column => column.Column.Contains("comparison", StringComparison.OrdinalIgnoreCase)));
            Assert.All(unit.Indexes, index => Assert.DoesNotContain(index.Columns, column => string.IsNullOrWhiteSpace(column.Column)));
        }
    }

    [Fact]
    public void Identity_comparison_algorithm_version_participates_in_the_target_fingerprint()
    {
        var units = WorkflowsDesignStorageManifest.CreateUnits();
        var ids = units.Select(unit => unit.Columns.Single(column => column.Name == WorkflowsDesignStorageManifest.IdField).MaxLength);
        Assert.All(ids, length => Assert.Equal(WorkflowsDesignStorageManifest.IdentityMaximumLength, length));
        Assert.DoesNotContain(units, unit => unit.Columns.Any(column => column.Name.Contains("folded", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(WorkflowsDesignStorageManifest.SchemaVersionMaximumLength,
            units.SelectMany(unit => unit.Columns).Where(column => column.Name == WorkflowsDesignStorageManifest.SchemaVersionField).Select(column => column.MaxLength).Distinct().Single());
        Assert.Equal(WorkflowsDesignStorageManifest.DefinitionIdSearchKeyMaximumLength,
            units.Single(unit => unit.Id.Value == WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind)
            .Columns.Single(column => column.Name == WorkflowsDesignStorageManifest.DefinitionIdSearchKeyField).MaxLength);
    }

    [Fact]
    public void Definition_id_search_route_is_within_the_public_portable_index_budget()
    {
        var definitions = WorkflowsDesignStorageManifest.Require(
            WorkflowsDesignStorageManifest.WorkflowDefinitionDocumentKind);
        var index = definitions.Indexes.Single(candidate =>
            candidate.Name == WorkflowsDesignStorageManifest.DefinitionByIdSearchIndex);
        var width = index.Columns.Sum(column =>
            definitions.Columns.Single(item => item.Name == column.Column).MaxLength!.Value * 2);
        Assert.Equal(WorkflowsDesignStorageManifest.DefinitionIdSearchKeyMaximumLength * 2, width);
        Assert.True(width <= PortabilityValidator.StrictIndexKeyByteBudget);
    }

    [Fact]
    public void No_unit_declares_a_by_collection_enumeration_route()
    {
        Assert.DoesNotContain(WorkflowsDesignStorageManifest.CreateUnits().SelectMany(unit => unit.Indexes), index =>
            index.Name is "by-collection" or "list-all");
    }

    private static void AssertIndex(StorageUnit unit, string name, IReadOnlyList<string> columns, bool unique)
    {
        var index = Assert.Single(unit.Indexes, candidate => candidate.Name == name);
        Assert.Equal(unique, index.IsUnique);
        Assert.Equal(columns, index.Columns.Select(column => column.Column));
    }
}
