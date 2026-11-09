using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Etl.Tests;

public sealed partial class EtlRuntimeJourneyTests
{
    [Fact]
    public async Task Actual_worker_lost_during_result_delivery_reclaims_the_same_owed_payload_without_consumption()
    {
        await using var files = new RuntimeFiles("id,name\n");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var (host, invocation) = await files.PrepareWorkerRunAsync(EtlTestPipelines.Create(), "process-delivery", deadline.Token);
        await using var operation = host.Services.CreateAsyncScope();
        var store = operation.ServiceProvider.GetRequiredService<IEtlRuntimeStore>();
        var terminal = Assert.IsType<EtlRunView>(await store.FindRunAsync(files.Scope, invocation.RunId, deadline.Token));
        Assert.Equal(EtlRunStatus.Succeeded, terminal.Status);
        Assert.NotNull(terminal.TerminalResult);
        var original = (await files.ReadOutboxAsync(host.Services, invocation.RunId))!;
        Assert.Equal(0, original.Attempts);
        Assert.Null(original.Disposition);
        Assert.Null(original.AcknowledgedAtUtcTicks);

        var predecessor = await files.StartWorkerAsync("delivery-predecessor", deadline.Token,
            storeGate: EtlWorkerStoreGate.AfterDeliveryClaim);
        var first = await predecessor.WaitForDeliveryAsync(deadline.Token);
        Assert.Equal(predecessor.ProcessId, first.ProcessId);
        Assert.NotEqual(Environment.ProcessId, first.ProcessId);
        Assert.Equal(invocation.ResultAddress, first.Lease.Address);
        Assert.Equal(terminal.TerminalResult, first.Lease.Result);
        var claimed = (await files.ReadOutboxAsync(host.Services, invocation.RunId))!;
        Assert.Equal(1, claimed.Attempts);
        Assert.Equal(first.Lease.Fence, claimed.Fence);
        Assert.Null(claimed.Disposition);
        Assert.Null(claimed.AcknowledgedAtUtcTicks);

        await predecessor.CrashAsync();
        Assert.True(predecessor.HasExited);
        var survivor = await files.StartWorkerAsync("delivery-survivor", deadline.Token,
            storeGate: EtlWorkerStoreGate.AfterDeliveryClaim);
        var retried = await survivor.WaitForDeliveryAsync(deadline.Token);
        Assert.Equal(survivor.ProcessId, retried.ProcessId);
        Assert.NotEqual(first.ProcessId, retried.ProcessId);
        Assert.NotEqual(first.Lease.Owner, retried.Lease.Owner);
        Assert.True(retried.Lease.Fence > first.Lease.Fence);
        Assert.Equal(first.Lease.DeliveryId, retried.Lease.DeliveryId);
        Assert.Equal(first.Lease.RunId, retried.Lease.RunId);
        Assert.Equal(first.Lease.Scope, retried.Lease.Scope);
        Assert.Equal(first.Lease.Address, retried.Lease.Address);
        Assert.Equal(first.Lease.Result, retried.Lease.Result);
        Assert.Equal(first.Lease.PayloadHash, retried.Lease.PayloadHash);
        var owed = (await files.ReadOutboxAsync(host.Services, invocation.RunId))!;
        Assert.Equal(2, owed.Attempts);
        Assert.Equal(retried.Lease.Fence, owed.Fence);
        Assert.Null(owed.Disposition);
        Assert.Null(owed.AcknowledgedAtUtcTicks);
        var unchanged = Assert.IsType<EtlRunView>(await store.FindRunAsync(files.Scope, invocation.RunId, deadline.Token));
        Assert.Equal((terminal.Status, terminal.TerminalResult, terminal.AcceptedRecords, terminal.RejectedRecords,
                terminal.FilteredRecords, terminal.OutstandingRecords, terminal.OutstandingBytes),
            (unchanged.Status, unchanged.TerminalResult, unchanged.AcceptedRecords, unchanged.RejectedRecords,
                unchanged.FilteredRecords, unchanged.OutstandingRecords, unchanged.OutstandingBytes));
        Assert.Empty(await store.ListManifestAsync(files.Scope, invocation.RunId, null, 10, deadline.Token));
        // Both real workers reached an adapter with no native consumer; neither is called consumed.
        await survivor.ReleaseAsync(deadline.Token); // The fixture adapter can only return null.
    }
}
