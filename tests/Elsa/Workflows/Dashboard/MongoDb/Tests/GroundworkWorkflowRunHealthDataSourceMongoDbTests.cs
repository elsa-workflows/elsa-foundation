using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Persistence.Groundwork.Stores;
using Elsa.Workflows.Dashboard.Persistence.Groundwork.MongoDb;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Core.Scoping;
using Groundwork.Documents.Scoping;
using Xunit;

namespace Elsa.Workflows.Dashboard.MongoDb.Tests;

/// <summary>
/// MongoDB counterpart of
/// <c>Elsa.Workflows.Dashboard.Tests.GroundworkWorkflowRunHealthDataSourceTests.SqliteAdapterPagesPastOneHundredExecutionsAndReturnsTheirIncidentsExactly</c>.
/// Lives in its own leaf project (see the project file comment) so the real-container run stays out of the
/// fast PR gate while the SQLite coverage in the main Dashboard test project keeps running there.
/// </summary>
[Collection(MongoDbDashboardContainerCollection.Name)]
public sealed class GroundworkWorkflowRunHealthDataSourceMongoDbTests(MongoDbDashboardContainerFixture fixture)
{
    [SkippableFact]
    public async Task MongoDbAdapterPagesPastOneHundredExecutionsAndReturnsTheirIncidentsExactly()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "Docker unavailable.");

        await using var client = await fixture.Driver.OpenClientAsync(
            ElsaRuntimeStorageManifest.Create(),
            DocumentStoreAccess.Scoped(new StorageScope("tenant-a")));
        var store = client.DocumentStore;

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

        var source = new MongoDbWorkflowRunHealthDataSource(() => fixture.Driver.CreateRawDatabase());
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
