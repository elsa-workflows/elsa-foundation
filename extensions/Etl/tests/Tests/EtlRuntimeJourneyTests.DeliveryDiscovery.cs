using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Persistence.EntityFrameworkCore;
using Elsa.Etl.Persistence.EntityFrameworkCore.Entities;
using Elsa.Etl.Persistence.EntityFrameworkCore.Stores;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Etl.Tests;

public sealed partial class EtlRuntimeJourneyTests
{
    [Fact]
    public async Task Pending_terminal_outbox_is_discovered_after_restart_without_file_binding_scopes()
    {
        await using var files = new RuntimeFiles("id,name\n1,Ada\n");
        var invocation = files.Invocation(EtlTestPipelines.Create(), "restart-without-bindings");
        var firstHost = await files.StartHostAsync(EfMigratePolicy.AutoMigrate);
        await files.AdmitAsync(firstHost, invocation);
        var terminal = await files.WaitForTerminalAsync(firstHost, invocation.RunId);
        Assert.NotNull(terminal.TerminalResult);
        var pending = Assert.IsType<EtlRuntimeOutboxRecord>(await files.ReadOutboxAsync(firstHost.Services, invocation.RunId));
        Assert.Null(pending.Disposition);
        Assert.Equal(0, pending.Attempts);

        await files.StopHostAsync(firstHost);
        var delivery = new TestResultDelivery(EtlDeliveryDisposition.Consumed);
        var restarted = await files.StartHostAsync(EfMigratePolicy.Validate, includeBindings: false, resultDelivery: delivery);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));

        var claimed = await delivery.WaitForDeliveryAsync(timeout.Token);
        Assert.Equal(invocation.Scope, claimed.Scope);
        Assert.Equal(invocation.RunId, claimed.RunId);
        Assert.Equal(terminal.TerminalResult, claimed.Result);
        var acknowledged = await files.WaitForOutboxAsync(restarted.Services, invocation.RunId,
            row => row.Disposition == (int)EtlDeliveryDisposition.Consumed, timeout.Token);
        Assert.True(acknowledged.Attempts >= 1);
        Assert.NotNull(acknowledged.AcknowledgedAtUtcTicks);
    }

    [Fact]
    public async Task Claim_next_delivery_refuses_an_outbox_whose_invocation_scope_does_not_match_its_row()
    {
        await using var files = new RuntimeFiles("id,name\n1,Ada\n");
        var host = await files.StartHostAsync(EfMigratePolicy.AutoMigrate);
        var invocation = files.Invocation(EtlTestPipelines.Create(), "scope-mismatch-delivery");

        await using (var operation = host.Services.CreateAsyncScope())
        {
            var services = operation.ServiceProvider;
            var store = services.GetRequiredService<IEtlRuntimeStore>();
            Assert.Equal(EtlStoreStatus.Applied,
                (await store.RejectAdmissionAsync(invocation, new EtlRunDiagnostic("etl.binding-unavailable"))).Status);

            var db = services.GetRequiredService<EtlRuntimeDbContext>();
            var row = await db.Runs.SingleAsync();
            row.InvocationJson = EtlRuntimeRecordCodec.Write(invocation with
            {
                Scope = new EtlScope(invocation.Scope.TenantId, "different-partition")
            });
            await db.SaveChangesAsync();

            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await store.ClaimNextDeliveryAsync("scope-validation-worker"));
        }

        var outbox = Assert.IsType<EtlRuntimeOutboxRecord>(await files.ReadOutboxAsync(host.Services, invocation.RunId));
        Assert.Equal(0, outbox.Attempts);
        Assert.Null(outbox.Owner);
        Assert.Null(outbox.ExpiresAtUtcTicks);
        Assert.Null(outbox.Disposition);
    }

    [Fact]
    public async Task Claim_next_delivery_skips_corrupt_earliest_scope_without_mutating_it()
    {
        await using var files = new RuntimeFiles("id,name\n1,Ada\n");
        var host = await files.StartHostAsync(EfMigratePolicy.AutoMigrate, includeBindings: false, maximumPageSize: 1);
        var orderedScopes = new[]
            {
                new EtlScope(null, "delivery-scope-a"),
                new EtlScope(null, "delivery-scope-b")
            }
            .OrderBy(EtlRuntimeIdentity.ScopeKey, StringComparer.Ordinal)
            .ToArray();
        var scopeA = orderedScopes[0];
        var scopeB = orderedScopes[1];
        var corrupt = files.Invocation(EtlTestPipelines.Create(), "corrupt-earliest") with { Scope = scopeA };
        var valid = files.Invocation(EtlTestPipelines.Create(), "valid-later") with { Scope = scopeB };

        await using var operation = host.Services.CreateAsyncScope();
        var services = operation.ServiceProvider;
        var store = services.GetRequiredService<IEtlRuntimeStore>();
        Assert.Equal(EtlStoreStatus.Applied,
            (await store.RejectAdmissionAsync(corrupt, new EtlRunDiagnostic("etl.binding-unavailable"))).Status);
        Assert.Equal(EtlStoreStatus.Applied,
            (await store.RejectAdmissionAsync(valid, new EtlRunDiagnostic("etl.binding-unavailable"))).Status);

        var db = services.GetRequiredService<EtlRuntimeDbContext>();
        var scopeAKey = EtlRuntimeIdentity.ScopeKey(scopeA);
        var runAKey = EtlRuntimeRecordCodec.EncodeIdentity(corrupt.RunId, "run id");
        var runA = await db.Runs.SingleAsync(row => row.ScopeKey == scopeAKey && row.RunKey == runAKey);
        runA.InvocationJson = EtlRuntimeRecordCodec.Write(corrupt with { Scope = scopeB });
        await db.SaveChangesAsync();

        var lease = Assert.IsType<EtlDeliveryLease>(await store.ClaimNextDeliveryAsync("global-worker"));
        Assert.Equal(scopeB, lease.Scope);
        Assert.Equal(valid.RunId, lease.RunId);
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await store.ClaimDeliveryAsync(scopeA, "scoped-worker"));

        var rows = await db.Outbox.AsNoTracking().ToListAsync();
        var outboxA = Assert.Single(rows, row => row.ScopeKey == scopeAKey);
        Assert.Equal(0, outboxA.Attempts);
        Assert.Equal(0, outboxA.Fence);
        Assert.Null(outboxA.Owner);
        Assert.Null(outboxA.ExpiresAtUtcTicks);
        Assert.Null(outboxA.Disposition);
        Assert.Null(outboxA.AcknowledgedAtUtcTicks);

        var scopeBKey = EtlRuntimeIdentity.ScopeKey(scopeB);
        var outboxB = Assert.Single(rows, row => row.ScopeKey == scopeBKey);
        Assert.Equal(1, outboxB.Attempts);
        Assert.Equal(lease.Fence, outboxB.Fence);
        Assert.Equal(EtlRuntimeRecordCodec.EncodeIdentity("global-worker", "worker id"), outboxB.Owner);
        Assert.NotNull(outboxB.ExpiresAtUtcTicks);
        Assert.Null(outboxB.Disposition);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Claim_delivery_rejects_retained_invocation_with_mismatched_run_id(bool useGlobalClaim)
    {
        await using var files = new RuntimeFiles("id,name\n1,Ada\n");
        var host = await files.StartHostAsync(EfMigratePolicy.AutoMigrate, includeBindings: false);
        var invocation = files.Invocation(EtlTestPipelines.Create(), "run-id-mismatch");

        await using var operation = host.Services.CreateAsyncScope();
        var services = operation.ServiceProvider;
        var store = services.GetRequiredService<IEtlRuntimeStore>();
        Assert.Equal(EtlStoreStatus.Applied,
            (await store.RejectAdmissionAsync(invocation, new EtlRunDiagnostic("etl.binding-unavailable"))).Status);

        var db = services.GetRequiredService<EtlRuntimeDbContext>();
        var run = await db.Runs.SingleAsync();
        var terminal = await db.Terminals.SingleAsync();
        var outbox = await db.Outbox.SingleAsync();
        run.InvocationJson = EtlRuntimeRecordCodec.Write(invocation with { RunId = "different-run-id" });
        outbox.PayloadHash = EtlRuntimeJson.ResultEnvelopeHash(run.InvocationJson, terminal.ResultJson);
        await db.SaveChangesAsync();

        if (useGlobalClaim)
            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await store.ClaimNextDeliveryAsync("run-id-validation-worker"));
        else
            await Assert.ThrowsAsync<InvalidDataException>(async () =>
                await store.ClaimDeliveryAsync(invocation.Scope, "run-id-validation-worker"));

        var retained = await db.Outbox.AsNoTracking().SingleAsync();
        Assert.Equal(0, retained.Attempts);
        Assert.Equal(0, retained.Fence);
        Assert.Null(retained.Owner);
        Assert.Null(retained.ExpiresAtUtcTicks);
        Assert.Null(retained.Disposition);
        Assert.Null(retained.AcknowledgedAtUtcTicks);
    }
}
