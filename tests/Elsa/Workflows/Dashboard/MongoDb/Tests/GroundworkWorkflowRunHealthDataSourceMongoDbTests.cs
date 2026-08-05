using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.Serialization;
using Elsa.Persistence.Groundwork.Stores;
using Elsa.Persistence.Groundwork.Testing;
using Elsa.Workflows.Dashboard.Persistence.Groundwork.MongoDb;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Core.Scoping;
using Groundwork.Documents.Scoping;
using Xunit;

namespace Elsa.Workflows.Dashboard.MongoDb.Tests;

/// <summary>
/// MongoDB counterpart of
/// <c>Elsa.Workflows.Dashboard.Tests.GroundworkWorkflowRunHealthDataSourceTests</c>.
/// Lives in its own leaf project (see the project file comment) so the real-container run stays out of the
/// fast PR gate while the SQLite coverage in the main Dashboard test project keeps running there.
/// </summary>
/// <remarks>
/// The fixture shares one database across the whole collection, so every test here picks scope names that no
/// other test uses. Two tests writing the same scope would see each other's documents and the counts would
/// depend on execution order.
/// </remarks>
[Collection(MongoDbDashboardContainerCollection.Name)]
public sealed class GroundworkWorkflowRunHealthDataSourceMongoDbTests(MongoDbDashboardContainerFixture fixture)
{
    [SkippableFact]
    public async Task MongoDbAdapterPagesPastOneHundredExecutionsAndReturnsTheirIncidentsExactly()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "Docker unavailable.");

        await using var client = await OpenAsync("tenant-a");
        var stores = Stores(client, "tenant-a");

        for (var index = 0; index < 125; index++)
            await stores.Executions.SaveAsync(Execution("tenant-a", index));
        await stores.Incidents.SaveAsync(Incident("incident-1", "run-124"));

        var snapshot = await QueryAsync("tenant-a");

        Assert.Equal(125, snapshot.StartedCount);
        Assert.Equal(1, snapshot.IncidentBearingRunCount);
        Assert.Equal(1, snapshot.IncidentCount);
    }

    /// <summary>
    /// Execution ids are not globally unique, so two tenants can legitimately hold the same one. An incident
    /// raised in one tenant must never be counted against the other tenant's identically-numbered execution.
    /// This is the MongoDB counterpart of the SQLite collision test, and it is what exercises the
    /// storage-scope-qualified <c>$lookup</c> against a real server.
    /// </summary>
    [SkippableFact]
    public async Task MongoDbAdapterExcludesAnotherTenantsIncidentOnACollidingExecutionId()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "Docker unavailable.");

        const string collidingExecutionId = "collision-run";
        await using var clientA = await OpenAsync(CollisionTenantA);
        await using var clientB = await OpenAsync(CollisionTenantB);

        // Both tenants own an execution under the very same id; only tenant B's execution has an incident.
        await Stores(clientA, CollisionTenantA).Executions
            .SaveAsync(Execution(CollisionTenantA, 0, collidingExecutionId));
        var tenantB = Stores(clientB, CollisionTenantB);
        await tenantB.Executions.SaveAsync(Execution(CollisionTenantB, 0, collidingExecutionId));
        await tenantB.Incidents.SaveAsync(Incident("incident-1", collidingExecutionId));

        var tenantASnapshot = await QueryAsync(CollisionTenantA);
        var tenantBSnapshot = await QueryAsync(CollisionTenantB);

        Assert.Equal(1, tenantASnapshot.StartedCount);
        Assert.Equal(0, tenantASnapshot.IncidentCount);
        Assert.Equal(0, tenantASnapshot.IncidentBearingRunCount);

        // Tenant B still sees its own incident: the scope predicate narrows the join, it does not break it.
        Assert.Equal(1, tenantBSnapshot.StartedCount);
        Assert.Equal(1, tenantBSnapshot.IncidentCount);
        Assert.Equal(1, tenantBSnapshot.IncidentBearingRunCount);
    }

    private const string CollisionTenantA = "collision-tenant-a";
    private const string CollisionTenantB = "collision-tenant-b";

    private ValueTask<GroundworkProviderClient> OpenAsync(string tenantId) =>
        fixture.Driver.OpenClientAsync(
            ElsaRuntimeStorageManifest.Create(),
            DocumentStoreAccess.Scoped(new StorageScope(tenantId)));

    private static TenantStores Stores(GroundworkProviderClient client, string tenantId)
    {
        var serializer = new GroundworkRuntimeDocumentSerializer();
        return new(
            new(client.DocumentStore, serializer, new FixedAccessContextAccessor(
                PersistenceAccessContext.Scoped(new PersistenceScope(tenantId)))),
            new(client.DocumentStore, serializer));
    }

    private async Task<WorkflowRunHealthSnapshot> QueryAsync(string tenantId)
    {
        var source = new MongoDbWorkflowRunHealthDataSource(() => fixture.Driver.CreateRawDatabase());
        return await new WorkflowRunHealthService(source).QueryAsync(new(
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch.AddDays(1),
            "Etc/UTC",
            WorkflowRunHealthBucketSize.Day,
            tenantId));
    }

    private sealed record TenantStores(
        GroundworkWorkflowExecutionStateStore Executions,
        GroundworkIncidentStateStore Incidents);

    private sealed class FixedAccessContextAccessor(PersistenceAccessContext current)
        : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = current;
    }

    private static WorkflowExecutionState Execution(string tenantId, int index, string? executionId = null)
    {
        var startedAt = DateTimeOffset.UnixEpoch.AddMinutes(index);
        return new(
            executionId ?? $"run-{index}",
            new WorkflowExecutableIdentity($"artifact-{index}", "definition", $"version-{index}", "1", "hash"),
            WorkflowExecutionStatus.Completed,
            null,
            startedAt,
            startedAt,
            startedAt.AddMinutes(1),
            startedAt.AddMinutes(1),
            null,
            null,
            tenantId,
            new Dictionary<string, string>());
    }

    private static IncidentState Incident(string id, string executionId) =>
        new(id, executionId, null, null, IncidentSeverity.Error, IncidentStatus.Open,
            null, "Failure", "Failed", DateTimeOffset.UnixEpoch, null);
}
