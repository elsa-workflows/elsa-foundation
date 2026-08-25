using Elsa.Activities.Design.Persistence.Groundwork;
using Elsa.Workflows.Design.Persistence.Groundwork;
using Groundwork.Kernel;
using Groundwork.Sqlite;
using Groundwork.Store;
using Xunit;

namespace Elsa.Workflows.Design.Persistence.Groundwork.Tests;

public sealed class DesignStorageManifestCurrentSchemaAdmissionTests
{
    /// <summary>
    /// Both design catalogs apply to a real SQLite database, and applying them again over the schema they
    /// just created is a no-op. Restart idempotence is the property under test: a host that reapplies its
    /// catalog on every start must not accumulate work or fail on the second pass.
    /// </summary>
    [Fact]
    public void Current_design_schema_is_applied_by_the_provider_and_restart_is_idempotent()
    {
        var databasePath = Path.Join(Path.GetTempPath(), $"elsa-current-design-schema-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={databasePath};Pooling=False";
        StorageUnit[] units =
        [
            .. WorkflowsDesignStorageManifest.CreateUnits(),
            .. ActivitiesDesignStorageManifest.CreateUnits()
        ];

        try
        {
            using (var connection = new SqliteProviderFactory().Create(connectionString))
            {
                foreach (var unit in units)
                    connection.Schema.Apply(unit);
            }

            // A second connection, as a restart would be: the schema is already there, and applying the
            // same catalog over it must settle rather than repeat or refuse.
            using (var connection = new SqliteProviderFactory().Create(connectionString))
            {
                foreach (var unit in units)
                    connection.Schema.Apply(unit);

                foreach (var unit in units)
                {
                    var session = connection.OpenSession(unit, StorageAccess.Scoped(new StorageScope("design-schema-admission")));
                    Assert.Null(session.Read(new StorageKey(new Dictionary<string, object?>
                    {
                        [WorkflowsDesignStorageManifest.IdField] = "absent"
                    })));
                }
            }
        }
        finally
        {
            File.Delete(databasePath);
        }
    }
}
