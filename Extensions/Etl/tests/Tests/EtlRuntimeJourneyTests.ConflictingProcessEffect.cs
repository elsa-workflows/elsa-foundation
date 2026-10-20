using System.Text;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Runtime.Execution;
using Elsa.Etl.Runtime.Hosting;
using Elsa.Persistence.EntityFramework;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Etl.Tests;

public sealed partial class EtlRuntimeJourneyTests
{
    [Fact]
    public async Task Actual_replacement_worker_refuses_conflicting_published_effect_without_advancing_cursor()
    {
        await using var files = new RuntimeFiles("id,name\n1,Ada\n");
        var host = await files.StartHostAsync(EfMigratePolicy.AutoMigrate);
        await host.Services.GetRequiredService<EtlRuntimeWorker>().StopAsync(CancellationToken.None);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        await using var operation = host.Services.CreateAsyncScope();
        var store = operation.ServiceProvider.GetRequiredService<IEtlRuntimeStore>();
        var execution = operation.ServiceProvider.GetRequiredService<EtlExecution>();
        var invocation = files.Invocation(EtlTestPipelines.Create() with
        {
            Limits = new EtlLimits { PartitionCount = 1 }
        }, "process-effect-conflict");
        Assert.Equal(EtlStoreStatus.Applied, (await execution.AdmitAsync(invocation, deadline.Token)).Status);
        var preparation = Assert.IsType<EtlPreparationLease>(await store.ClaimPreparationAsync(files.Scope, "prepare", deadline.Token));
        await execution.PrepareAsync(preparation, deadline.Token);

        var predecessor = await files.StartWorkerAsync("publisher", deadline.Token);
        var published = await predecessor.WaitForAcceptanceAsync(deadline.Token);
        Assert.Equal(predecessor.ProcessId, published.ProcessId);
        var effect = Assert.Single(published.Acceptance.Effects);
        var path = Path.Combine(files.OutputRoot, effect.Chunk.Key);
        Assert.Equal("{\"name\":\"Ada\"}\n", await File.ReadAllTextAsync(path, deadline.Token));
        var before = Assert.IsType<EtlRunView>(await store.FindRunAsync(files.Scope, invocation.RunId, deadline.Token));
        var old = Assert.Single(await store.ListPartitionsAsync(files.Scope, invocation.RunId, -1, 1, deadline.Token));
        Assert.Equal(published.Lease.Fence, old.Fence);
        Assert.Equal(published.Lease.Owner, old.Owner);
        Assert.NotNull(old.OwedBatch);
        Assert.Equal(1, before.OutstandingRecords);
        Assert.True(before.OutstandingBytes > 0);
        Assert.Empty(await store.ListManifestAsync(files.Scope, invocation.RunId, null, 10, deadline.Token));

        await predecessor.CrashAsync();
        Assert.True(predecessor.HasExited);
        // A stopped writer cannot race this controlled fixture corruption of its unaccepted file.
        const string conflict = "{\"name\":\"Eve\"}\n";
        Assert.Equal(effect.Chunk.ByteLength, Encoding.UTF8.GetByteCount(conflict));
        await File.WriteAllTextAsync(path, conflict, new UTF8Encoding(false), deadline.Token);
        var survivor = await files.StartWorkerAsync("conflict-survivor", deadline.Token);
        Assert.NotEqual(predecessor.ProcessId, survivor.ProcessId);
        var terminal = await files.WaitForTerminalAsync(host, invocation.RunId);

        Assert.Equal(EtlRunStatus.Faulted, terminal.Status);
        Assert.Equal("etl.effect-conflict", terminal.TerminalResult!.Diagnostic!.Code);
        Assert.Equal(0, terminal.AcceptedRecords);
        Assert.Equal(0, terminal.RejectedRecords);
        Assert.Equal(0, terminal.FilteredRecords);
        Assert.Equal(before.OutstandingRecords, terminal.OutstandingRecords);
        Assert.Equal(before.OutstandingBytes, terminal.OutstandingBytes);
        var retained = Assert.Single(await store.ListPartitionsAsync(files.Scope, invocation.RunId, -1, 1, deadline.Token));
        Assert.True(retained.Fence > old.Fence);
        Assert.Equal(old.NextOrdinal, retained.NextOrdinal);
        Assert.Equal(old.OwedBatch, retained.OwedBatch);
        Assert.Empty(await store.ListManifestAsync(files.Scope, invocation.RunId, null, 10, deadline.Token));
        Assert.False(File.Exists(survivor.AcceptancePath));
        Assert.Equal(conflict, await File.ReadAllTextAsync(path, deadline.Token));
        var delivery = Assert.IsType<EtlDeliveryLease>(await store.ClaimDeliveryAsync(files.Scope, "inspect-owed-result", deadline.Token));
        Assert.Equal(terminal.TerminalResult, delivery.Result);
        Assert.Equal(invocation.ResultAddress, delivery.Address);
        // No native parent is configured; the fault remains owed rather than being called consumed.
    }
}
