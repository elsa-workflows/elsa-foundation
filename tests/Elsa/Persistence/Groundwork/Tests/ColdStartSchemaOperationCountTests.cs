using Elsa.Activities.Design.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Querying;
using Elsa.Persistence.Groundwork.Unified.Composition;
using Elsa.Secrets.Persistence.Groundwork;
using Elsa.Studio.Preferences.Persistence.Groundwork;
using Elsa.Workflows.Design.Persistence.Groundwork;
using Elsa.Workflows.Publishing.Persistence.Groundwork;
using Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork;
using Groundwork.Core.Capabilities;
using Groundwork.Core.SchemaEvolution;
using Groundwork.Sqlite;
using Groundwork.Sqlite.PhysicalStorage;
using Microsoft.Data.Sqlite;
using Xunit;
using Xunit.Abstractions;

namespace Elsa.Persistence.Groundwork.Tests;

/// <summary>
/// Deterministic cold-start schema baseline (spec 129, First-Request/Cold-Start Readiness).
///
/// The reference <c>Elsa.Server</c> host admits <see cref="RuntimeGroundworkStorageManifestSource"/> plus the
/// seven other feature families selected by <c>GroundworkAllFeaturesDeploymentSchema</c> against one SQLite file
/// on the FIRST matching request (default <c>AutoApplyOnStartup = true</c>). This test reconstructs the exact
/// SQLite physical target from that same manifest-source set and pins the number of DDL operations that fresh-DB
/// admission applies. The count is load-independent, so it is the anchor evidence for the cold-start baseline
/// report and the regression guard the later skip-if-current unit (program unit 3) will compare against.
/// Identity is intentionally excluded because the reference host persists ASP.NET Core Identity through EF Core,
/// not Groundwork (shells.json enables <c>FoundationIdentityAspNetCoreIdentityEntityFrameworkCore</c>).
/// </summary>
public sealed class ColdStartSchemaOperationCountTests(ITestOutputHelper output)
{
    // Pinned fresh-database operation count for the reference deployment schema on SQLite. When a feature adds
    // or removes a storage unit / index this number changes on purpose — update it together with the baseline
    // report (docs/reports/cold-start-readiness-2026-07.md) and re-review the cold-start budget.
    private const int ExpectedFreshDatabaseOperationCount = 963;

    private static IReadOnlyList<IGroundworkStorageManifestSource> ReferenceDeploymentSources() =>
    [
        new RuntimeGroundworkStorageManifestSource(),
        new SecretsGroundworkStorageManifestSource(),
        new StudioPreferencesGroundworkStorageManifestSource(),
        new DistributedGroundworkStorageManifestSource(),
        new WorkflowsDesignGroundworkStorageManifestSource(),
        new ActivitiesDesignGroundworkStorageManifestSource(),
        new GroundworkDesignAtomicWriteStorageManifestSource(),
        new PublishingGroundworkStorageManifestSource()
    ];

    [Fact]
    public async Task Fresh_database_admission_applies_the_pinned_operation_count_and_warm_restart_is_a_no_op()
    {
        var databasePath = Path.Join(Path.GetTempPath(), $"elsa-cold-start-schema-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={databasePath}";

        try
        {
            var source = await CreateSqliteSchemaSourceAsync(ReferenceDeploymentSources());

            int appliedOnFreshDatabase;
            await using (var connection = new SqliteConnection(connectionString))
            {
                var admission = await source.InspectRuntimeAdmissionAsync(
                    new SqlitePhysicalSchemaExecutor(connection),
                    new GroundworkRuntimeSchemaAdmissionOptions { AutoApplyOnStartup = true });

                Assert.True(
                    admission.IsReady,
                    string.Join(Environment.NewLine, admission.Diagnostics.Select(x => x.Message)));
                appliedOnFreshDatabase = admission.AppliedOperationCount;
            }

            output.WriteLine($"Fresh-database applied schema operations: {appliedOnFreshDatabase}");

            // Warm restart against the already-admitted database: nothing pending, nothing applied.
            await using (var connection = new SqliteConnection(connectionString))
            {
                var warmAdmission = await source.InspectRuntimeAdmissionAsync(
                    new SqlitePhysicalSchemaExecutor(connection),
                    new GroundworkRuntimeSchemaAdmissionOptions { AutoApplyOnStartup = true });

                Assert.True(warmAdmission.IsReady);
                Assert.Equal(0, warmAdmission.AppliedOperationCount);
                Assert.Empty(warmAdmission.PendingOperations);
            }

            Assert.True(
                appliedOnFreshDatabase > 0,
                "Fresh-database admission should apply at least one schema operation.");
            Assert.Equal(ExpectedFreshDatabaseOperationCount, appliedOnFreshDatabase);
        }
        finally
        {
            if (File.Exists(databasePath))
                File.Delete(databasePath);
        }
    }

    private static ValueTask<GroundworkPhysicalSchemaManifestSource> CreateSqliteSchemaSourceAsync(
        IReadOnlyCollection<IGroundworkStorageManifestSource> manifestSources)
    {
        var topology = new GroundworkProviderTopologySnapshot(
            SqliteGroundworkCapabilities.Provider.Name,
            "sqlite-file",
            new HashSet<string>(StringComparer.Ordinal)
            {
                RuntimeGroundworkStorageManifestSource.MultiDocumentTransactionsTopologyIdentity
            });
        return GroundworkStoreInitialization.CreatePhysicalSchemaSourceAsync(
            SqliteGroundworkCapabilities.Runtime(),
            topology,
            SqliteGroundworkCapabilities.PhysicalNames,
            manifestSources);
    }
}
