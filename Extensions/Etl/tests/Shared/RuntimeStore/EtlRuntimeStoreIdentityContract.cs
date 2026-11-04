using System.Data.Common;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Etl.Tests;

// Run identities and check constraints on every engine (spec 1203): the server engines key runs by a hash of the id.
public abstract partial class EtlRuntimeStoreContract
{
    [SkippableFact]
    public async Task Run_ids_of_maximum_length_or_differing_only_by_case_are_distinct_runs()
    {
        await using var database = await CreateDatabaseAsync();
        await using var context = database.CreateContext();
        var store = database.CreateStore(context);
        var scope = new EtlScope("tenant-identity", "partition-identity");
        string[] runIds = ["Run-A", "run-a", new string('r', EtlRuntimeEfModule.IdentityMaximumLength)];

        foreach (var runId in runIds)
            Assert.Equal(EtlStoreStatus.Applied, (await store.AdmitAsync(EtlRuntimeFixtures.Admission(runId, scope))).Status);

        foreach (var runId in runIds)
            Assert.Equal(runId, (await store.FindRunAsync(scope, runId))!.Invocation.RunId);
        var first = await store.ListRunsAsync(scope, afterRunId: null, take: 2);
        var rest = await store.ListRunsAsync(scope, first[^1].Invocation.RunId, take: 2);
        Assert.Equal(runIds.Order(StringComparer.Ordinal), first.Concat(rest).Select(run => run.Invocation.RunId).Order(StringComparer.Ordinal));
    }

    [SkippableFact]
    public async Task Check_constraints_refuse_impossible_connector_run_counters()
    {
        await using var database = await CreateDatabaseAsync();
        await using var context = database.CreateContext();
        var connector = (IEtlConnectorRuntimeStore)database.CreateStore(context);
        Assert.Equal(EtlStoreStatus.Applied,
            (await connector.AdmitConnectorAsync(ConnectorAdmission("constraints", new EtlScope("tenant-constraints", "partition")))).Status);

        await Assert.ThrowsAnyAsync<DbException>(() => context.ConnectorRuns
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.RetainedOutputBytes, -1L)));
        await Assert.ThrowsAnyAsync<DbException>(() => context.ConnectorRuns
            .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.CompletionRequestedAtUtcTicks, 0L)));
    }
}
