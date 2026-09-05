using Groundwork.Kernel;
using Groundwork.Kernel.Schema;
using Groundwork.Sqlite;
using Xunit;

namespace Elsa.Activities.Design.Persistence.Groundwork.Tests;

public sealed class ActivityDefinitionVersionSqliteBoundaryTests
{
    [Fact]
    public void Version_projection_uses_a_new_versioned_table_for_the_clean_schema_boundary()
    {
        var path = Path.Combine(Path.GetTempPath(), $"elsa-activity-design-v2-boundary-{Guid.NewGuid():N}.db");
        try
        {
            using var connection = new SqliteProviderFactory().Create($"Data Source={path};Pooling=False");
            var current = ActivitiesDesignStorageManifest.Require(
                ActivitiesDesignStorageManifest.ActivityDefinitionVersionDocumentKind);
            var legacy = current with
            {
                Name = "elsa_activity_definition_versions",
                SchemaVersion = ActivitiesDesignStorageManifest.StorageSchemaVersion,
                Columns = current.Columns.Select(column =>
                    column.Name is ActivitiesDesignStorageManifest.ActivityDefinitionVersionDefinitionIdField or
                        ActivitiesDesignStorageManifest.ActivityDefinitionVersionSemVerSortKeyField
                        ? column with { IsNullable = true }
                        : column).ToArray(),
                Indexes =
                [
                    new IndexDefinition
                    {
                        Name = ActivitiesDesignStorageManifest.ActivityDefinitionVersionByDefinitionIndex,
                        Columns =
                        [
                            new IndexColumn(ActivitiesDesignStorageManifest.ActivityDefinitionVersionDefinitionIdField),
                            new IndexColumn(ActivitiesDesignStorageManifest.ActivityDefinitionVersionSemVerSortKeyField),
                            new IndexColumn(ActivitiesDesignStorageManifest.ActivityDefinitionVersionIdField)
                        ]
                    },
                    new IndexDefinition
                    {
                        Name = "activity_definition_version_by_definition_and_sort_key",
                        Columns =
                        [
                            new IndexColumn(ActivitiesDesignStorageManifest.ActivityDefinitionVersionDefinitionIdField),
                            new IndexColumn(ActivitiesDesignStorageManifest.ActivityDefinitionVersionSemVerSortKeyField)
                        ]
                    }
                ]
            };

            connection.Schema.Apply(legacy);
            var refusal = Assert.Throws<PhysicalSchemaPlanRefusedException>(() => connection.Schema.Apply(current));

            Assert.Contains("Rebuild the target from the current declaration", refusal.Message, StringComparison.Ordinal);
            Assert.Equal("elsa_activity_definition_versions_v2", current.Name);
            Assert.Equal(ActivitiesDesignStorageManifest.ActivityDefinitionVersionStorageSchemaVersion, current.SchemaVersion);
        }
        finally
        {
            foreach (var file in new[] { path, $"{path}-shm", $"{path}-wal" })
                if (File.Exists(file))
                    File.Delete(file);
        }
    }
}
