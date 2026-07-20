using Groundwork.Core.Indexing;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Queries;
using Xunit;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Tests;

public sealed class WorkflowsDesignStorageManifestTests
{
    [Fact]
    public void Manifest_compiles_all_workflow_design_routes_to_scoped_physical_entity_tables()
    {
        var manifest = WorkflowsDesignStorageManifest.Create();
        var resolution = PhysicalStorageResolver.Resolve(
            manifest,
            PhysicalNamePolicy.Identity,
            ProviderPhysicalNameNormalizer.Identity);

        Assert.True(
            resolution.IsValid,
            string.Join(Environment.NewLine, resolution.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.Equal(4, resolution.Definitions.Count);
        Assert.All(resolution.Definitions, definition =>
        {
            Assert.Equal(PhysicalStorageForm.PhysicalEntityTable, definition.Definition.Form);
            Assert.Equal("storage_scope", definition.Definition.Envelope!.StorageScopeColumn);
            Assert.Contains(definition.Definition.Indexes, index =>
                index.Columns.First().ColumnLogicalName == "storage_scope");
        });
    }

    [Fact]
    public void Manifest_declares_only_bounded_physical_routes_with_complete_index_evidence()
    {
        var manifest = WorkflowsDesignStorageManifest.Create();
        var expectedRoutes = new[]
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
        };

        Assert.Equal(
            expectedRoutes.Order(StringComparer.Ordinal),
            manifest.StorageUnits
                .SelectMany(unit => unit.PhysicalStorage!.BoundedQueries)
                .Select(query => query.Identity)
                .Order(StringComparer.Ordinal));

        foreach (var unit in manifest.StorageUnits)
        {
            var storage = Assert.IsType<StorageUnitPhysicalStorage>(unit.PhysicalStorage);
            var table = Assert.IsType<PhysicalStoragePolicy.ExplicitPolicy>(storage.Policy).Definition;

            Assert.Equal(StorageUnitProvisioningMode.Declared, storage.ProvisioningMode);
            Assert.Empty(unit.Indexes);
            Assert.Empty(unit.Queries);
            Assert.All(storage.BoundedQueries, query =>
            {
                Assert.Equal(BoundedQueryExecutionClass.ScaleBearing, query.ExecutionClass);
                Assert.NotEmpty(query.PredicateFields);
                Assert.NotEmpty(query.ResultOperations);
                Assert.Contains(storage.LogicalIndexes, index => index.Identity == query.IndexIdentity);
                Assert.Contains(table.Indexes, index => index.LogicalName == query.IndexIdentity);
            });
        }
    }

    [Fact]
    public void Version_and_draft_routes_have_deterministic_ordering_and_bounded_in_support()
    {
        var routes = WorkflowsDesignStorageManifest.Create().StorageUnits
            .SelectMany(unit => unit.PhysicalStorage!.BoundedQueries)
            .ToDictionary(query => query.Identity, StringComparer.Ordinal);

        var versions = routes[WorkflowsDesignStorageManifest.ListVersionsByDefinitionQuery];
        Assert.Contains(
            versions.PredicateFields,
            field => field.Operations.Contains(PortableQueryOperation.In));
        Assert.Equal(
            [
                new BoundedQuerySortField(WorkflowsDesignStorageManifest.VersionDefinitionIdField, PhysicalSortDirection.Ascending),
                new BoundedQuerySortField(WorkflowsDesignStorageManifest.VersionSemVerSortKeyField, PhysicalSortDirection.Ascending),
                new BoundedQuerySortField(WorkflowsDesignStorageManifest.VersionIdField, PhysicalSortDirection.Ascending)
            ],
            versions.SortFields);

        var latest = routes[WorkflowsDesignStorageManifest.FindLatestVersionQuery];
        Assert.Equal(
            [
                new BoundedQuerySortField(WorkflowsDesignStorageManifest.VersionSemVerSortKeyField, PhysicalSortDirection.Descending),
                new BoundedQuerySortField(WorkflowsDesignStorageManifest.VersionIdField, PhysicalSortDirection.Descending)
            ],
            latest.SortFields);

        var drafts = routes[WorkflowsDesignStorageManifest.ListDraftsByDefinitionQuery];
        Assert.Contains(
            drafts.PredicateFields,
            field => field.Operations.Contains(PortableQueryOperation.In));
        Assert.Equal(
            [
                new BoundedQuerySortField(WorkflowsDesignStorageManifest.DraftDefinitionIdField, PhysicalSortDirection.Ascending),
                new BoundedQuerySortField(WorkflowsDesignStorageManifest.DraftLastModifiedAtField, PhysicalSortDirection.Descending),
                new BoundedQuerySortField(WorkflowsDesignStorageManifest.DraftCreatedAtField, PhysicalSortDirection.Descending),
                new BoundedQuerySortField(WorkflowsDesignStorageManifest.DraftIdField, PhysicalSortDirection.Descending)
            ],
            drafts.SortFields);
    }
}
