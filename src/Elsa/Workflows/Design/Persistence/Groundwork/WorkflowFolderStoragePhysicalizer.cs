using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Groundwork.Core.Indexing;
using Groundwork.Core.Manifests;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Queries;

namespace Elsa.Workflows.Design.Persistence.Groundwork;

/// <summary>Installs the provider-side direct-child keyset browse route for workflow folders.</summary>
internal static class WorkflowFolderStoragePhysicalizer
{
    public static StorageManifest AddRoute(StorageManifest manifest) => manifest with
    {
        StorageUnits = manifest.StorageUnits.Select(unit =>
            unit.Identity.Value == WorkflowsDesignStorageManifest.WorkflowFolderDocumentKind ? Physicalize(unit) : unit).ToArray()
    };

    private static StorageUnit Physicalize(StorageUnit unit)
    {
        var storage = unit.PhysicalStorage ?? throw new InvalidOperationException("Workflow-folder browsing requires physical storage.");
        var envelope = new DocumentEnvelopeDefinition();
        var index = new LogicalIndexDeclaration(
            WorkflowsDesignStorageManifest.WorkflowFolderBrowseIndex,
            [
                new IndexField(WorkflowsDesignStorageManifest.WorkflowFolderParentKeyField, IndexValueKind.Keyword),
                new IndexField(WorkflowsDesignStorageManifest.WorkflowFolderNormalizedNameField, IndexValueKind.Keyword),
                new IndexField("entity.id", IndexValueKind.Keyword)
            ], IndexValueKind.Keyword, false, MissingValueBehavior.Excluded);
        var unique = new LogicalIndexDeclaration(
            "by-parent-and-normalized-name",
            [
                new IndexField(WorkflowsDesignStorageManifest.WorkflowFolderParentKeyField, IndexValueKind.Keyword),
                new IndexField(WorkflowsDesignStorageManifest.WorkflowFolderNormalizedNameField, IndexValueKind.Keyword)
            ],
            IndexValueKind.Keyword,
            isUnique: true,
            MissingValueBehavior.Excluded);
        var definition = PhysicalTableDefinition.PhysicalEntityTable(
            "workflow_folders",
            [
                Column(
                    "parent_key",
                    WorkflowsDesignStorageManifest.WorkflowFolderParentKeyField,
                    WorkflowFolderNames.MaximumIdentifierLength + WorkflowFolder.ParentFolderKeyPrefix.Length),
                Column("normalized_name", WorkflowsDesignStorageManifest.WorkflowFolderNormalizedNameField, WorkflowFolderNames.MaximumNameLength),
                Column("folder_id", "entity.id", WorkflowFolderNames.MaximumIdentifierLength)
            ], envelope,
            [new PhysicalIndexDefinition(unique.Identity,
                [
                    new PhysicalIndexColumnDefinition(envelope.StorageScopeColumn, 0),
                    new PhysicalIndexColumnDefinition("parent_key", 1),
                    new PhysicalIndexColumnDefinition("normalized_name", 2)
                ], isUnique: true),
             new PhysicalIndexDefinition(index.Identity,
                [
                    new PhysicalIndexColumnDefinition(envelope.StorageScopeColumn, 0),
                    new PhysicalIndexColumnDefinition("parent_key", 1),
                    new PhysicalIndexColumnDefinition("normalized_name", 2),
                    new PhysicalIndexColumnDefinition("folder_id", 3),
                    new PhysicalIndexColumnDefinition(envelope.IdLookupKeyColumn, 4)
                ])]);
        var query = new BoundedQueryDeclaration(
            WorkflowsDesignStorageManifest.PageWorkflowFoldersQuery,
            index.Identity,
            new HashSet<PortableQueryOperation> { PortableQueryOperation.Equal },
            QuerySortSupport.Ascending,
            QueryPagingSupport.Cursor,
            BoundedQueryExecutionClass.ScaleBearing,
            false,
            false,
            [
                new BoundedQuerySortField(WorkflowsDesignStorageManifest.WorkflowFolderNormalizedNameField, PhysicalSortDirection.Ascending),
                new BoundedQuerySortField("entity.id", PhysicalSortDirection.Ascending)
            ]);
        return unit with
        {
            PhysicalStorage = new StorageUnitPhysicalStorage(
            StorageUnitProvisioningMode.Declared,
            PhysicalStoragePolicy.Explicit(definition),
            storage.LogicalIndexes
                .Where(item => item.Identity != unique.Identity && item.Identity != index.Identity)
                .Append(unique)
                .Append(index)
                .ToArray(),
            storage.BoundedQueries.Where(item => item.Identity != query.Identity).Append(query).ToArray(),
            storage.NameOverrides,
            storage.BoundedMutations)
        };
    }

    private static ProjectedColumnDefinition Column(string name, string path, int length) => new(
        name, path, PortablePhysicalType.String, Length: length);
}
