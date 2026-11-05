using System.Data.Common;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Persistence.EntityFrameworkCore;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Etl.Tests;

// Run identities and check constraints on every engine: the server engines key runs by a hash of the id.
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

    [SkippableTheory]
    [InlineData(" ")]
    [InlineData(" untrimmed ")]
    [InlineData("control\nid")]
    public async Task Delivery_discovery_isolates_an_invalid_persisted_run_id_without_consuming_it(string invalidRunId)
    {
        await using var database = await CreateDatabaseAsync();
        await using var context = database.CreateContext();
        var clock = new MutableTimeProvider();
        var store = database.CreateStore(context, clock);
        var scopes = new[]
            {
                new EtlScope("tenant-identity", "invalid-delivery-a"),
                new EtlScope("tenant-identity", "invalid-delivery-b")
            }
            .OrderBy(EtlRuntimeIdentity.ScopeKey, StringComparer.Ordinal)
            .ToArray();
        var corrupt = EtlRuntimeFixtures.Admission("corrupt-earliest", scopes[0]).Invocation;
        var valid = EtlRuntimeFixtures.Admission("valid-later", scopes[1]).Invocation;
        foreach (var invocation in new[] { corrupt, valid })
            Assert.Equal(EtlStoreStatus.Applied,
                (await store.RejectAdmissionAsync(invocation, new EtlRunDiagnostic("etl.binding-unavailable"))).Status);

        var corruptScopeKey = EtlRuntimeIdentity.ScopeKey(corrupt.Scope);
        var corruptRun = await context.Runs.SingleAsync(row => row.ScopeKey == corruptScopeKey);
        var encodedInvalidRunId = EfRelationalIdentity.Encode(invalidRunId);
        corruptRun.RunId = encodedInvalidRunId;
        await context.SaveChangesAsync();

        // Caller mistakes remain argument errors; only retained data is classified as corruption.
        await Assert.ThrowsAsync<ArgumentException>(async () => await store.FindRunAsync(corrupt.Scope, invalidRunId));
        var lease = Assert.IsType<EtlDeliveryLease>(await store.ClaimNextDeliveryAsync("global-worker"));
        Assert.Equal(valid.Scope, lease.Scope);
        Assert.Equal(valid.RunId, lease.RunId);
        await Assert.ThrowsAsync<InvalidDataException>(async () => await store.ClaimDeliveryAsync(corrupt.Scope, "scoped-worker"));
        await Assert.ThrowsAsync<InvalidDataException>(async () => await store.ClaimNextDeliveryAsync("next-worker"));

        var rows = await context.Outbox.AsNoTracking().ToListAsync();
        Assert.Equal(2, rows.Count);
        var corruptOutbox = Assert.Single(rows, row => row.ScopeKey == corruptScopeKey);
        Assert.Equal(0, corruptOutbox.Attempts);
        Assert.Equal(0, corruptOutbox.Fence);
        Assert.Null(corruptOutbox.Owner);
        Assert.Null(corruptOutbox.ExpiresAtUtcTicks);
        Assert.Null(corruptOutbox.Disposition);
        Assert.Null(corruptOutbox.AcknowledgedAtUtcTicks);
        Assert.Equal(encodedInvalidRunId,
            (await context.Runs.AsNoTracking().SingleAsync(row => row.ScopeKey == corruptScopeKey)).RunId);

        var validOutbox = Assert.Single(rows, row => row.ScopeKey == EtlRuntimeIdentity.ScopeKey(valid.Scope));
        Assert.Equal(1, validOutbox.Attempts);
        Assert.Equal(lease.Fence, validOutbox.Fence);
        Assert.Equal(EfRelationalIdentity.Encode("global-worker"), validOutbox.Owner);
        Assert.NotNull(validOutbox.ExpiresAtUtcTicks);
        Assert.Null(validOutbox.Disposition);
        Assert.Null(validOutbox.AcknowledgedAtUtcTicks);
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
