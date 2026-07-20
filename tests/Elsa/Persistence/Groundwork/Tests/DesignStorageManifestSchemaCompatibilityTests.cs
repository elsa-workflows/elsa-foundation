using Elsa.Activities.Design.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Testing;
using Elsa.Persistence.Groundwork.Unified.Composition;
using Elsa.Workflows.Design.Persistence.Groundwork;
using Groundwork.Core.Capabilities;
using Groundwork.Core.Manifests;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.Queries;
using Groundwork.Core.SchemaEvolution;
using Groundwork.Sqlite;
using Groundwork.Sqlite.PhysicalStorage;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Elsa.Persistence.Groundwork.Tests;

public sealed class DesignStorageManifestSchemaCompatibilityTests
{
    [Fact]
    public async Task Deterministic_design_query_successors_upgrade_legacy_sqlite_target_additively_and_restart_idempotently()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"elsa-design-schema-successors-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={databasePath}";

        try
        {
            var current = CurrentManifests();
            var legacy = current.Select(CreateLegacyManifest).ToArray();
            var legacySource = await CreateSqliteSchemaSourceAsync(legacy);
            await using (var connection = new SqliteConnection(connectionString))
            {
                var applied = await PhysicalSchemaApplication.ApplyAsync(
                    legacySource.PhysicalTarget,
                    new SqlitePhysicalSchemaExecutor(connection));

                Assert.NotEqual(PhysicalSchemaApplicationOutcome.Rejected, applied.Outcome);
                Assert.NotEqual(PhysicalSchemaApplicationOutcome.AuthorizationRequired, applied.Outcome);
            }

            var currentSource = await CreateSqliteSchemaSourceAsync(current);
            await using (var connection = new SqliteConnection(connectionString))
            {
                var admission = await currentSource.InspectRuntimeAdmissionAsync(
                    new SqlitePhysicalSchemaExecutor(connection),
                    new GroundworkRuntimeSchemaAdmissionOptions { AutoApplyOnStartup = true });

                Assert.True(admission.IsReady, string.Join(Environment.NewLine, admission.Diagnostics.Select(x => x.Message)));
                Assert.True(admission.AppliedOperationCount > 0);
                Assert.DoesNotContain(admission.Diagnostics, diagnostic => diagnostic.Code == "ELSA-GW-SCHEMA-DRIFT");
            }

            await using (var connection = new SqliteConnection(connectionString))
            {
                var restart = await currentSource.InspectRuntimeAdmissionAsync(
                    new SqlitePhysicalSchemaExecutor(connection),
                    new GroundworkRuntimeSchemaAdmissionOptions { AutoApplyOnStartup = true });

                Assert.True(restart.IsReady, string.Join(Environment.NewLine, restart.Diagnostics.Select(x => x.Message)));
                Assert.Equal(0, restart.AppliedOperationCount);
                Assert.Empty(restart.PendingOperations);
            }
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    private static StorageManifest[] CurrentManifests() =>
    [
        ActivitiesDesignStorageManifest.Create(),
        WorkflowsDesignStorageManifest.Create()
    ];

    private static StorageManifest CreateLegacyManifest(StorageManifest current) => current with
    {
        StorageUnits = current.StorageUnits.Select(CreateLegacyUnit).ToArray()
    };

    private static StorageUnit CreateLegacyUnit(StorageUnit unit)
    {
        if (unit.PhysicalStorage is not { } storage ||
            storage.Policy is not PhysicalStoragePolicy.ExplicitPolicy explicitPolicy)
            return unit;

        var legacyIndexes = storage.LogicalIndexes
            .Where(index => !IsSuccessorIndex(index.Identity))
            .ToArray();
        var definition = explicitPolicy.Definition;
        var legacyTable = PhysicalTableDefinition.PhysicalEntityTable(
            definition.FeatureDefaultLogicalName!,
            definition.ProjectedColumns,
            definition.Envelope,
            definition.Indexes.Where(index => !IsSuccessorIndex(index.LogicalName)).ToArray(),
            definition.SchemaVersion,
            definition.Evolution);
        var legacyQueries = storage.BoundedQueries
            .Select(CreateLegacyQuery)
            .ToArray();

        return unit with
        {
            PhysicalStorage = new StorageUnitPhysicalStorage(
                storage.ProvisioningMode,
                PhysicalStoragePolicy.Explicit(legacyTable),
                legacyIndexes,
                legacyQueries,
                storage.NameOverrides,
                storage.BoundedMutations)
        };
    }

    private static bool IsSuccessorIndex(string indexIdentity) =>
        indexIdentity.EndsWith(ActivitiesDesignStorageManifest.DeterministicDocumentOrderIndexSuffix, StringComparison.Ordinal) ||
        indexIdentity == WorkflowsDesignStorageManifest.DeterministicCollectionIndex;

    private static string LegacyIndexIdentity(string indexIdentity) =>
        indexIdentity.EndsWith(ActivitiesDesignStorageManifest.DeterministicDocumentOrderIndexSuffix, StringComparison.Ordinal)
            ? indexIdentity[..^ActivitiesDesignStorageManifest.DeterministicDocumentOrderIndexSuffix.Length]
            : indexIdentity == WorkflowsDesignStorageManifest.DeterministicCollectionIndex
                ? WorkflowsDesignStorageManifest.ByCollectionIndex
                : indexIdentity;

    private static BoundedQueryDeclaration CreateLegacyQuery(BoundedQueryDeclaration query)
    {
        var wasSuccessor = IsSuccessorIndex(query.IndexIdentity);
        return new BoundedQueryDeclaration(
            query.Identity,
            LegacyIndexIdentity(query.IndexIdentity),
            query.Operations,
            wasSuccessor ? QuerySortSupport.None : query.SortSupport,
            query.PagingSupport,
            query.ExecutionClass,
            query.SupportsDisjunction,
            query.SupportsTotalCount,
            wasSuccessor ? null : query.SortFields,
            query.PredicateFields,
            query.ResultOperations,
            query.LatestPerKeyPath,
            query.ResidualPredicateFields);
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
