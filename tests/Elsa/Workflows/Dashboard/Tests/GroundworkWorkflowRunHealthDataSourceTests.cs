using Elsa.Persistence.Groundwork;
using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Persistence.Groundwork.Stores;
using Elsa.Workflows.Dashboard.Persistence.Groundwork;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Core.Capabilities;
using Groundwork.Core.Scoping;
using Groundwork.Documents.Scoping;
using Groundwork.Sqlite.Documents;
using Microsoft.Data.Sqlite;
using Xunit;

namespace Elsa.Workflows.Dashboard.Tests;

public sealed class GroundworkWorkflowRunHealthDataSourceTests
{
    [Fact]
    public async Task SqliteAdapterPagesPastOneHundredExecutionsAndReturnsTheirIncidentsExactly()
    {
        var path = Path.Join(Path.GetTempPath(), $"elsa-dashboard-{Guid.NewGuid():N}.db");
        try
        {
            var store = await SqliteDocumentStoreFactory.CreateAsync(
                $"Data Source={path}",
                ElsaRuntimeStorageManifest.Create(),
                new ProviderIdentity("groundwork-sqlite", "1.0.0"),
                DocumentStoreAccess.Scoped(new StorageScope("tenant-a")));
            var serializer = new GroundworkRuntimeDocumentSerializer();
            var executionStore = new GroundworkWorkflowExecutionStateStore(
                store,
                serializer,
                new FixedAccessContextAccessor(
                    PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))));
            var incidentStore = new GroundworkIncidentStateStore(store, serializer);

            for (var index = 0; index < 125; index++)
                await executionStore.SaveAsync(Execution(index));
            await incidentStore.SaveAsync(Incident("incident-1", "run-124"));

            var source = new GroundworkWorkflowRunHealthDataSource(
                () => new SqliteConnection($"Data Source={path}"),
                GroundworkRunHealthDialect.Sqlite);
            var service = new WorkflowRunHealthService(source);
            var snapshot = await service.QueryAsync(new(
                DateTimeOffset.UnixEpoch,
                DateTimeOffset.UnixEpoch.AddDays(1),
                "Etc/UTC",
                WorkflowRunHealthBucketSize.Day,
                "tenant-a"));

            Assert.Equal(125, snapshot.StartedCount);
            Assert.Equal(1, snapshot.IncidentBearingRunCount);
            Assert.Equal(1, snapshot.IncidentCount);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private sealed class FixedAccessContextAccessor(PersistenceAccessContext current)
        : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = current;
    }

    private static WorkflowExecutionState Execution(int index)
    {
        var startedAt = DateTimeOffset.UnixEpoch.AddMinutes(index);
        return new(
            $"run-{index}",
            new WorkflowExecutableIdentity($"artifact-{index}", "definition", $"version-{index}", "1", "hash"),
            WorkflowExecutionStatus.Completed,
            null,
            startedAt,
            startedAt,
            startedAt.AddMinutes(1),
            startedAt.AddMinutes(1),
            null,
            null,
            "tenant-a",
            new Dictionary<string, string>());
    }

    private static IncidentState Incident(string id, string executionId) =>
        new(id, executionId, null, null, IncidentSeverity.Error, IncidentStatus.Open,
            null, "Failure", "Failed", DateTimeOffset.UnixEpoch, null);

}
