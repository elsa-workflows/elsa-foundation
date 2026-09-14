using Groundwork.Kernel;
using Groundwork.Kernel.Schema;
using Groundwork.Sqlite;
using Xunit;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Tests;

public sealed class WorkflowVersionAndDraftSqliteBoundaryTests
{
    [Fact]
    public void Version_projection_uses_a_new_versioned_table_for_the_clean_schema_boundary()
    {
        var path = Path.Combine(Path.GetTempPath(), $"elsa-workflow-version-v2-boundary-{Guid.NewGuid():N}.db");
        try
        {
            using var connection = new SqliteProviderFactory().Create($"Data Source={path};Pooling=False");
            var current = WorkflowsDesignStorageManifest.Require(
                WorkflowsDesignStorageManifest.WorkflowDefinitionVersionDocumentKind);
            var legacy = current with
            {
                Name = "elsa_workflow_definition_versions",
                SchemaVersion = 1,
                Columns = current.Columns
                    .Where(column => column.Name != WorkflowsDesignStorageManifest.VersionDefinitionIdLookupHashField)
                    .ToArray(),
                Indexes =
                [
                    new IndexDefinition
                    {
                        Name = WorkflowsDesignStorageManifest.VersionByDefinitionIndex,
                        IsUnique = true,
                        Columns =
                        [
                            new IndexColumn(WorkflowsDesignStorageManifest.VersionDefinitionIdField),
                            new IndexColumn(WorkflowsDesignStorageManifest.VersionSemVerSortKeyField),
                            new IndexColumn(WorkflowsDesignStorageManifest.VersionIdField)
                        ]
                    },
                    new IndexDefinition
                    {
                        Name = WorkflowsDesignStorageManifest.VersionByDefinitionAndSortKeyIndex,
                        IsUnique = true,
                        Columns =
                        [
                            new IndexColumn(WorkflowsDesignStorageManifest.VersionDefinitionIdField),
                            new IndexColumn(WorkflowsDesignStorageManifest.VersionSemVerSortKeyField)
                        ]
                    },
                    new IndexDefinition
                    {
                        Name = WorkflowsDesignStorageManifest.LatestVersionByDefinitionIndex,
                        Columns =
                        [
                            new IndexColumn(WorkflowsDesignStorageManifest.VersionDefinitionIdField),
                            new IndexColumn(WorkflowsDesignStorageManifest.VersionSemVerSortKeyField, SortDirection.Descending),
                            new IndexColumn(WorkflowsDesignStorageManifest.VersionIdField, SortDirection.Descending)
                        ]
                    }
                ]
            };

            connection.Schema.Apply(legacy);
            var refusal = Assert.Throws<PhysicalSchemaPlanRefusedException>(() => connection.Schema.Apply(current));

            Assert.Contains("Rebuild the target from the current declaration", refusal.Message, StringComparison.Ordinal);
            Assert.Equal("elsa_workflow_definition_versions_v2", current.Name);
            Assert.Equal(WorkflowsDesignStorageManifest.VersionStorageSchemaVersion, current.SchemaVersion);
        }
        finally
        {
            foreach (var file in new[] { path, $"{path}-shm", $"{path}-wal" })
                if (File.Exists(file))
                    File.Delete(file);
        }
    }

    [Fact]
    public void Draft_projection_uses_a_new_versioned_table_for_the_clean_schema_boundary()
    {
        var path = Path.Combine(Path.GetTempPath(), $"elsa-workflow-draft-v2-boundary-{Guid.NewGuid():N}.db");
        try
        {
            using var connection = new SqliteProviderFactory().Create($"Data Source={path};Pooling=False");
            var current = WorkflowsDesignStorageManifest.Require(
                WorkflowsDesignStorageManifest.WorkflowDefinitionDraftDocumentKind);
            var legacy = current with
            {
                Name = "elsa_workflow_definition_drafts",
                SchemaVersion = 1,
                Columns = current.Columns
                    .Where(column => column.Name != WorkflowsDesignStorageManifest.DraftDefinitionIdLookupHashField)
                    .ToArray(),
                Indexes =
                [
                    new IndexDefinition
                    {
                        Name = WorkflowsDesignStorageManifest.DraftByDefinitionIndex,
                        IsUnique = true,
                        Columns =
                        [
                            new IndexColumn(WorkflowsDesignStorageManifest.DraftDefinitionIdField),
                            new IndexColumn(WorkflowsDesignStorageManifest.DraftLastModifiedAtField, SortDirection.Descending),
                            new IndexColumn(WorkflowsDesignStorageManifest.DraftCreatedAtField, SortDirection.Descending),
                            new IndexColumn(WorkflowsDesignStorageManifest.DraftIdField, SortDirection.Descending)
                        ]
                    }
                ]
            };

            connection.Schema.Apply(legacy);
            var refusal = Assert.Throws<PhysicalSchemaPlanRefusedException>(() => connection.Schema.Apply(current));

            Assert.Contains("Rebuild the target from the current declaration", refusal.Message, StringComparison.Ordinal);
            Assert.Equal("elsa_workflow_definition_drafts_v2", current.Name);
            Assert.Equal(WorkflowsDesignStorageManifest.DraftStorageSchemaVersion, current.SchemaVersion);
        }
        finally
        {
            foreach (var file in new[] { path, $"{path}-shm", $"{path}-wal" })
                if (File.Exists(file))
                    File.Delete(file);
        }
    }
}
