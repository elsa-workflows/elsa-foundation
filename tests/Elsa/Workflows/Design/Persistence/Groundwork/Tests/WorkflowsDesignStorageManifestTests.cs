using Groundwork.Core.Capabilities;
using Groundwork.Core.Indexing;
using Groundwork.Core.Manifests;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Queries;
using Groundwork.Core.SchemaEvolution;
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

    [Fact]
    public void Exact_version_route_enforces_uniqueness_on_definition_and_semver_sort_key_only()
    {
        var versionUnit = WorkflowsDesignStorageManifest.Create().StorageUnits.Single(unit =>
            unit.Identity.Value == WorkflowsDesignStorageManifest.WorkflowDefinitionVersionDocumentKind);
        var storage = Assert.IsType<StorageUnitPhysicalStorage>(versionUnit.PhysicalStorage);
        var table = Assert.IsType<PhysicalStoragePolicy.ExplicitPolicy>(storage.Policy).Definition;
        var index = Assert.Single(storage.LogicalIndexes, candidate =>
            candidate.Identity == "version-by-definition-and-sort-key");
        var physicalIndex = Assert.Single(table.Indexes, candidate => candidate.LogicalName == index.Identity);

        Assert.True(index.IsUnique);
        Assert.Equal(
            [
                WorkflowsDesignStorageManifest.VersionDefinitionIdField,
                WorkflowsDesignStorageManifest.VersionSemVerSortKeyField
            ],
            index.Fields.Select(field => field.Path));
        Assert.True(physicalIndex.IsUnique);
        Assert.Equal(
            ["storage_scope", "definition_id", "sem_ver_sort_key"],
            physicalIndex.Columns.Select(column => column.ColumnLogicalName));
    }

    [Fact]
    public void Offset_routes_use_unique_entity_identity_tuples_without_the_wide_provider_comparison_key()
    {
        var comparisonKey = new DocumentEnvelopeDefinition().IdComparisonKeyColumn;
        foreach (var unit in WorkflowsDesignStorageManifest.Create().StorageUnits)
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
    public void Identity_comparison_algorithm_version_participates_in_the_target_fingerprint()
    {
        var baselineManifest = WorkflowsDesignStorageManifest.Create();
        var changedManifest = baselineManifest with
        {
            StorageUnits = baselineManifest.StorageUnits.Select(unit =>
                unit.Identity.Value == WorkflowsDesignStorageManifest.WorkflowDefinitionVersionDocumentKind
                    ? unit with
                    {
                        IdentityPolicy = IdentityPolicy.StringId(
                            stringCasePolicy: StringIdentityCasePolicy.UnicodeOrdinalIgnoreCase)
                    }
                    : unit).ToArray()
        };
        var provider = new ProviderIdentity("groundwork-test", "1.0.0");

        var baseline = PhysicalSchemaTargetCompiler.Compile(
            baselineManifest,
            provider,
            ProviderPhysicalNameNormalizer.Identity);
        var changed = PhysicalSchemaTargetCompiler.Compile(
            changedManifest,
            provider,
            ProviderPhysicalNameNormalizer.Identity);
        var baselineIdentity = baseline.Routes.Single(route =>
            route.StorageUnit.Value == WorkflowsDesignStorageManifest.WorkflowDefinitionVersionDocumentKind)
            .Envelope.Identity;
        var changedIdentity = changed.Routes.Single(route =>
            route.StorageUnit.Value == WorkflowsDesignStorageManifest.WorkflowDefinitionVersionDocumentKind)
            .Envelope.Identity;

        Assert.NotEqual(baselineIdentity.ComparisonAlgorithmId, changedIdentity.ComparisonAlgorithmId);
        Assert.Equal(baselineIdentity.LookupAlgorithmId, changedIdentity.LookupAlgorithmId);
        Assert.NotEqual(baseline.Fingerprint, changed.Fingerprint);
    }

    [Fact]
    public void No_unit_declares_a_by_collection_enumeration_route()
    {
        foreach (var unit in WorkflowsDesignStorageManifest.Create().StorageUnits)
        {
            var storage = Assert.IsType<StorageUnitPhysicalStorage>(unit.PhysicalStorage);
            var table = Assert.IsType<PhysicalStoragePolicy.ExplicitPolicy>(storage.Policy).Definition;

            Assert.DoesNotContain(storage.LogicalIndexes, index => index.Identity == "by-collection");
            Assert.DoesNotContain(table.Indexes, index => index.LogicalName == "by-collection");
            Assert.All(storage.BoundedQueries, query =>
                Assert.NotEqual("list-all", query.Identity));
            Assert.Empty(unit.Indexes);
            Assert.Empty(unit.Queries);
        }
    }
}
