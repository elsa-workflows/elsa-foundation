using Elsa.Activities.Design.Persistence.Groundwork;
using Elsa.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Testing;
using Elsa.Persistence.Groundwork.Unified.Composition;
using Elsa.Workflows.Design.Persistence.Groundwork;
using Groundwork.Core.Capabilities;
using Groundwork.Core.Manifests;
using Groundwork.Core.SchemaEvolution;
using Groundwork.Sqlite;
using Groundwork.Sqlite.PhysicalStorage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Elsa.Persistence.Groundwork.Tests;

public sealed class DesignStorageManifestCurrentSchemaAdmissionTests
{
    [Fact]
    public async Task Current_design_schema_is_admitted_by_the_host_target_and_restart_is_idempotent()
    {
        var databasePath = Path.Join(Path.GetTempPath(), $"elsa-current-design-schema-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={databasePath}";

        try
        {
            var source = await CreateSqliteSchemaSourceAsync(
            [
                ActivitiesDesignStorageManifest.Create(),
                WorkflowsDesignStorageManifest.Create()
            ]);
            await using (var connection = new SqliteConnection(connectionString))
            {
                var applied = await PhysicalSchemaApplication.ApplyAsync(
                    source.PhysicalTarget,
                    new SqlitePhysicalSchemaExecutor(connection));

                Assert.NotEqual(PhysicalSchemaApplicationOutcome.Rejected, applied.Outcome);
                Assert.NotEqual(PhysicalSchemaApplicationOutcome.AuthorizationRequired, applied.Outcome);
            }

            await using (var connection = new SqliteConnection(connectionString))
            {
                var admission = await source.InspectRuntimeAdmissionAsync(
                    new SqlitePhysicalSchemaExecutor(connection),
                    new GroundworkRuntimeSchemaAdmissionOptions { AutoApplyOnStartup = true });

                Assert.True(admission.IsReady, string.Join(Environment.NewLine, admission.Diagnostics.Select(x => x.Message)));
                Assert.Equal(0, admission.AppliedOperationCount);
                Assert.Empty(admission.PendingOperations);
            }
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    private static ValueTask<GroundworkPhysicalSchemaManifestSource> CreateSqliteSchemaSourceAsync(
        IEnumerable<StorageManifest> manifests)
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
            manifests
                .Select((manifest, index) => (IGroundworkStorageManifestSource)new StaticManifestSource($"design-schema-{index}", manifest))
                .ToArray());
    }

    private sealed class StaticManifestSource(string featureIdentity, StorageManifest manifest) : IGroundworkStorageManifestSource
    {
        public string FeatureIdentity => featureIdentity;

        public ValueTask<GroundworkStorageManifestDeclaration> CreateDeclarationAsync(
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new GroundworkStorageManifestDeclaration(
                FeatureIdentity,
                manifest,
                [],
                [],
                [],
                []));
    }
}
