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

public sealed class GroundworkWorkflowRunHealthDataSourceTests : IAsyncDisposable
{
    private readonly string _path = Path.Join(Path.GetTempPath(), $"elsa-dashboard-{Guid.NewGuid():N}.db");
    private readonly GroundworkRuntimeDocumentSerializer _serializer = new();

    [Fact]
    public async Task SqliteAdapterPagesPastOneHundredExecutionsAndReturnsTheirIncidentsExactly()
    {
        var tenant = await StoresAsync("tenant-a");
        for (var index = 0; index < 125; index++)
            await tenant.Executions.SaveAsync(Execution("tenant-a", index));
        await tenant.Incidents.SaveAsync(Incident("incident-1", "run-124"));

        var snapshot = await QueryAsync("tenant-a");

        Assert.Equal(125, snapshot.StartedCount);
        Assert.Equal(1, snapshot.IncidentBearingRunCount);
        Assert.Equal(1, snapshot.IncidentCount);
    }

    /// <summary>
    /// Execution ids are not globally unique, so two tenants can legitimately hold the same one. An incident
    /// raised in one tenant must never be counted against the other tenant's identically-numbered execution.
    /// </summary>
    [Fact]
    public async Task SqliteAdapterExcludesAnotherTenantsIncidentOnACollidingExecutionId()
    {
        const string collidingExecutionId = "run-0";
        var tenantA = await StoresAsync("tenant-a");
        var tenantB = await StoresAsync("tenant-b");

        // Both tenants own an execution under the very same id; only tenant B's execution has an incident.
        await tenantA.Executions.SaveAsync(Execution("tenant-a", 0));
        await tenantB.Executions.SaveAsync(Execution("tenant-b", 0));
        await tenantB.Incidents.SaveAsync(Incident("incident-1", collidingExecutionId));

        var tenantASnapshot = await QueryAsync("tenant-a");
        var tenantBSnapshot = await QueryAsync("tenant-b");

        Assert.Equal(1, tenantASnapshot.StartedCount);
        Assert.Equal(0, tenantASnapshot.IncidentCount);
        Assert.Equal(0, tenantASnapshot.IncidentBearingRunCount);

        // Tenant B still sees its own incident: the scope predicate narrows the join, it does not break it.
        Assert.Equal(1, tenantBSnapshot.StartedCount);
        Assert.Equal(1, tenantBSnapshot.IncidentCount);
        Assert.Equal(1, tenantBSnapshot.IncidentBearingRunCount);
    }

    private async Task<TenantStores> StoresAsync(string tenantId)
    {
        // The Groundwork dashboard SQL still reads workflow definitions, drafts and executions out of the shared
        // groundwork_documents table. Since wave 3 every manifest declares its physical storage, and a physically
        // opened store routes those kinds into dedicated tables (workflowDefinition, workflowDefinitionDraft,
        // workflow_execution_states) instead — so this fixture can only reproduce what the data source reads by
        // opening on the retired portable surface. The data source is what has to change; until it does, this open
        // stays portable and the package bump will surface it here.
        #pragma warning disable GW0005
        var store = await SqliteDocumentStoreFactory.CreateAsync(
            $"Data Source={_path}",
            ElsaRuntimeStorageManifest.Create(),
            new ProviderIdentity("groundwork-sqlite", "1.0.0"),
            DocumentStoreAccess.Scoped(new StorageScope(tenantId)));
        #pragma warning restore GW0005
        return new(
            new(store, _serializer, new FixedAccessContextAccessor(
                PersistenceAccessContext.Scoped(new PersistenceScope(tenantId)))),
            new(store, _serializer));
    }

    private async Task<WorkflowRunHealthSnapshot> QueryAsync(string tenantId)
    {
        var source = new GroundworkWorkflowRunHealthDataSource(
            () => new SqliteConnection($"Data Source={_path}"),
            GroundworkRunHealthDialect.Sqlite);
        return await new WorkflowRunHealthService(source).QueryAsync(new(
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch.AddDays(1),
            "Etc/UTC",
            WorkflowRunHealthBucketSize.Day,
            tenantId));
    }

    public ValueTask DisposeAsync()
    {
        File.Delete(_path);
        return ValueTask.CompletedTask;
    }

    private sealed record TenantStores(
        GroundworkWorkflowExecutionStateStore Executions,
        GroundworkIncidentStateStore Incidents);

    private sealed class FixedAccessContextAccessor(PersistenceAccessContext current)
        : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = current;
    }

    private static WorkflowExecutionState Execution(string tenantId, int index)
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
            tenantId,
            new Dictionary<string, string>());
    }

    private static IncidentState Incident(string id, string executionId) =>
        new(id, executionId, null, null, IncidentSeverity.Error, IncidentStatus.Open,
            null, "Failure", "Failed", DateTimeOffset.UnixEpoch, null);
}
