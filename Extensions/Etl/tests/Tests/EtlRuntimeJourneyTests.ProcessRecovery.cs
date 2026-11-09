using System.Security.Cryptography;
using System.Text;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Persistence.EntityFrameworkCore;
using Elsa.Etl.Runtime.Execution;
using Elsa.Etl.Runtime.Hosting;
using Elsa.Etl.Tests.Support;
using Elsa.Persistence.EntityFramework;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Etl.Tests;

public sealed partial class EtlRuntimeJourneyTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Actual_worker_interrupted_mid_staged_write_recovers_without_accepting_partial_content(bool snapshot)
    {
        const int records = 800;
        var names = Enumerable.Range(1, records).Select(index => $"person-{index}").ToArray();
        var source = "id,name\n" + string.Concat(names.Select((name, index) => $"{index % 3 + 1},{name}\n"));
        var jsonl = string.Concat(names.Select(name => $"{{\"name\":\"{name}\"}}\n"));
        var expected = Encoding.UTF8.GetBytes(snapshot ? source : jsonl);
        await using var files = new RuntimeFiles(source);
        var host = await files.StartHostAsync(EfMigratePolicy.AutoMigrate);
        await host.Services.GetRequiredService<EtlRuntimeWorker>().StopAsync(CancellationToken.None);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        await using var operation = host.Services.CreateAsyncScope();
        var store = operation.ServiceProvider.GetRequiredService<IEtlRuntimeStore>();
        var execution = operation.ServiceProvider.GetRequiredService<EtlExecution>();
        var pipeline = EtlTestPipelines.Create() with { Limits = new EtlLimits { PartitionCount = 1 } };
        var invocation = files.Invocation(pipeline, "mid-write");
        Assert.Equal(EtlStoreStatus.Applied, (await execution.AdmitAsync(invocation, deadline.Token)).Status);
        if (!snapshot)
        {
            var preparation = Assert.IsType<EtlPreparationLease>(await store.ClaimPreparationAsync(files.Scope, "prepare", deadline.Token));
            await execution.PrepareAsync(preparation, deadline.Token);
        }
        var predecessor = await files.StartWorkerAsync("writer", deadline.Token,
            snapshot ? EtlWorkerWritePause.SourceSnapshot : EtlWorkerWritePause.OutputChunk);
        var witness = await predecessor.WaitForWriteAsync(deadline.Token);
        Assert.Equal(predecessor.ProcessId, witness.ProcessId);
        Assert.NotEqual(Environment.ProcessId, witness.ProcessId);
        Assert.Equal(snapshot ? ".csv" : ".jsonl", witness.Extension);
        Assert.Equal(snapshot ? files.SnapshotRoot : files.OutputRoot, witness.OutputRoot);
        Assert.InRange(witness.PrefixBytes, 1, witness.WriteBytes - 1);
        if (snapshot)
            Assert.True(expected.Length > witness.WriteBytes); // The real source copy still has unread input.
        else
            Assert.Equal(expected.Length, witness.WriteBytes);
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(witness.EffectIdentity))).ToLowerInvariant() + witness.Extension;
        var finalPath = Path.Combine(witness.OutputRoot, key);
        Assert.False(File.Exists(finalPath));
        // Enumeration follows an explicit flushed-prefix marker; it does not race a staging-file poll.
        var abandonedStaging = Assert.Single(Directory.EnumerateFiles(witness.OutputRoot, "*.staging"));
        var before = Assert.IsType<EtlRunView>(await store.FindRunAsync(files.Scope, invocation.RunId, deadline.Token));
        var beforePartitions = await store.ListPartitionsAsync(files.Scope, invocation.RunId, -1, 1, deadline.Token);
        var db = operation.ServiceProvider.GetRequiredService<EtlRuntimeDbContext>();
        var preparationFence = (await db.Runs.AsNoTracking().SingleAsync(deadline.Token)).PreparationFence;
        if (snapshot)
        {
            Assert.Equal(EtlRunStatus.Preparing, before.Status);
            Assert.Null(before.Prepared);
            Assert.Empty(beforePartitions);
            Assert.Equal(0, before.OutstandingRecords);
            Assert.Equal(0, before.OutstandingBytes);
        }
        else
        {
            Assert.Equal(EtlRunStatus.Running, before.Status);
            Assert.NotNull(Assert.Single(beforePartitions).OwedBatch);
            Assert.Equal(records, before.OutstandingRecords);
            Assert.True(before.OutstandingBytes > 0);
        }
        Assert.Empty(await store.ListManifestAsync(files.Scope, invocation.RunId, null, 10, deadline.Token));
        await predecessor.CrashAsync();
        Assert.True(predecessor.HasExited);
        Assert.False(File.Exists(finalPath));
        var partial = await File.ReadAllBytesAsync(abandonedStaging, deadline.Token);
        Assert.Equal(witness.PrefixBytes, partial.Length);
        Assert.Equal(expected.AsSpan(0, partial.Length).ToArray(), partial);

        // Inspect the stopped writer's files before starting a replacement that can publish them.
        var survivor = await files.StartWorkerAsync("survivor", deadline.Token);
        Assert.False(survivor.HasExited);
        Assert.NotEqual(predecessor.ProcessId, survivor.ProcessId);
        var recovered = await survivor.WaitForAcceptanceAsync(deadline.Token);
        Assert.Equal(survivor.ProcessId, recovered.ProcessId);
        Assert.Equal(records, recovered.Acceptance.AcceptedRecords);
        var effect = Assert.Single(recovered.Acceptance.Effects);
        var retry = Assert.IsType<EtlRunView>(await store.FindRunAsync(files.Scope, invocation.RunId, deadline.Token));
        if (snapshot)
        {
            Assert.True((await db.Runs.AsNoTracking().SingleAsync(deadline.Token)).PreparationFence > preparationFence);
            Assert.Equal(key, retry.Prepared!.Source.Key);
            Assert.Equal(Convert.ToHexString(SHA256.HashData(expected)).ToLowerInvariant(), retry.Prepared.Source.ContentHash);
        }
        else
        {
            var old = Assert.Single(beforePartitions);
            Assert.True(recovered.Lease.Fence > old.Fence);
            Assert.NotEqual(old.Owner, recovered.Lease.Owner);
            Assert.Equal(old.NextOrdinal, recovered.Lease.NextOrdinal);
            Assert.Equal(old.OwedBatch, Assert.Single(await store.ListPartitionsAsync(files.Scope, invocation.RunId, -1, 1, deadline.Token)).OwedBatch);
            Assert.Equal(before.OutstandingRecords, retry.OutstandingRecords);
            Assert.Equal(before.OutstandingBytes, retry.OutstandingBytes);
            Assert.Equal(key, effect.Chunk.Key);
        }
        Assert.Equal(expected, await File.ReadAllBytesAsync(finalPath, deadline.Token));
        Assert.Empty(await store.ListManifestAsync(files.Scope, invocation.RunId, null, 10, deadline.Token));
        await survivor.ReleaseAsync(deadline.Token);
        var terminal = await files.WaitForTerminalAsync(host, invocation.RunId);
        Assert.Equal(EtlRunStatus.Succeeded, terminal.Status);
        Assert.Equal(records, terminal.AcceptedRecords);
        Assert.Equal(0, terminal.OutstandingRecords);
        Assert.Equal(0, terminal.OutstandingBytes);
        var accepted = Assert.Single(await store.ListManifestAsync(files.Scope, invocation.RunId, null, 10, deadline.Token));
        Assert.Equal(effect, accepted.Effect);
        Assert.Equal(jsonl, await files.ReadRouteAsync([accepted], EtlOutputRoute.Main));
        // Recovery must not delete another attempt's staging based only on a lease timeout.
        Assert.Equal(partial, await File.ReadAllBytesAsync(abandonedStaging, deadline.Token));
    }

    [Fact]
    public async Task Two_real_worker_processes_recover_a_published_unaccepted_effect_after_predecessor_exit()
    {
        await using var files = new RuntimeFiles("id,name\n1,Ada\n");
        var seed = await files.StartHostAsync(EfMigratePolicy.AutoMigrate);
        await seed.Services.GetRequiredService<EtlRuntimeWorker>().StopAsync(CancellationToken.None);
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        await using var operation = seed.Services.CreateAsyncScope();
        var store = operation.ServiceProvider.GetRequiredService<IEtlRuntimeStore>();
        var execution = operation.ServiceProvider.GetRequiredService<EtlExecution>();
        var invocation = files.Invocation(EtlTestPipelines.Create(), "process-recovery");
        Assert.Equal(EtlStoreStatus.Applied, (await execution.AdmitAsync(invocation, deadline.Token)).Status);
        var preparation = Assert.IsType<EtlPreparationLease>(await store.ClaimPreparationAsync(files.Scope, "fixture-prepare", deadline.Token));
        await execution.PrepareAsync(preparation, deadline.Token);

        var predecessor = await files.StartWorkerAsync("predecessor", deadline.Token);
        var old = await predecessor.WaitForAcceptanceAsync(deadline.Token);
        Assert.Equal(predecessor.ProcessId, old.ProcessId);
        Assert.NotEqual(Environment.ProcessId, old.ProcessId);
        var physical = Assert.Single(old.Acceptance.Effects);
        Assert.Equal("{\"name\":\"Ada\"}\n", await File.ReadAllTextAsync(Path.Combine(files.OutputRoot, physical.Chunk.Key), deadline.Token));
        var owed = Assert.Single(await store.ListPartitionsAsync(files.Scope, invocation.RunId, -1, 1, deadline.Token)).OwedBatch;
        Assert.NotNull(owed);
        Assert.Equal(old.Acceptance.Batch, owed.Batch);
        Assert.False(owed.Settled);
        var before = Assert.IsType<EtlRunView>(await store.FindRunAsync(files.Scope, invocation.RunId, deadline.Token));
        Assert.Equal(1, before.OutstandingRecords);
        Assert.True(before.OutstandingBytes > 0);
        Assert.Empty(await store.ListManifestAsync(files.Scope, invocation.RunId, null, 10, deadline.Token));

        var survivor = await files.StartWorkerAsync("survivor", deadline.Token);
        Assert.NotEqual(predecessor.ProcessId, survivor.ProcessId);
        Assert.False(predecessor.HasExited);
        Assert.False(survivor.HasExited);
        var held = Assert.Single(await store.ListPartitionsAsync(files.Scope, invocation.RunId, -1, 1, deadline.Token));
        Assert.Equal(old.Lease.Owner, held.Owner);
        Assert.Equal(old.Lease.Fence, held.Fence);
        await predecessor.CrashAsync();
        Assert.True(predecessor.HasExited);
        // The dead worker can no longer renew. Use the persisted expiry sampled while it still
        // owned the partition; after exit the survivor may already have claimed, so do not assert
        // the old fence remains visible during an unsynchronized inspection window.
        Assert.NotNull(held.ExpiresAt);

        var recovered = await survivor.WaitForAcceptanceAsync(deadline.Token);
        Assert.Equal(survivor.ProcessId, recovered.ProcessId);
        Assert.True(recovered.Lease.Fence > old.Lease.Fence);
        Assert.NotEqual(old.Lease.Owner, recovered.Lease.Owner);
        Assert.Equal(old.Lease.NextOrdinal, recovered.Lease.NextOrdinal);
        Assert.True(DateTimeOffset.UtcNow >= held.ExpiresAt.Value);
        Assert.Equal(old.Acceptance, recovered.Acceptance with { Effects = old.Acceptance.Effects });
        Assert.Equal(old.Acceptance.Effects, recovered.Acceptance.Effects);
        var retry = Assert.IsType<EtlRunView>(await store.FindRunAsync(files.Scope, invocation.RunId, deadline.Token));
        Assert.Equal(before.OutstandingRecords, retry.OutstandingRecords);
        Assert.Equal(before.OutstandingBytes, retry.OutstandingBytes);
        Assert.Equal(owed, Assert.Single(await store.ListPartitionsAsync(files.Scope, invocation.RunId, -1, 1, deadline.Token)).OwedBatch);
        Assert.Empty(await store.ListManifestAsync(files.Scope, invocation.RunId, null, 10, deadline.Token));
        await survivor.ReleaseAsync(deadline.Token);

        var terminal = await files.WaitForTerminalAsync(seed, invocation.RunId);
        Assert.Equal(EtlRunStatus.Succeeded, terminal.Status);
        Assert.Equal(1, terminal.AcceptedRecords);
        Assert.Equal(0, terminal.OutstandingRecords);
        Assert.Equal(0, terminal.OutstandingBytes);
        var manifest = Assert.Single(await store.ListManifestAsync(files.Scope, invocation.RunId, null, 10, deadline.Token));
        Assert.Equal(owed.Batch.BatchId, manifest.BatchId);
        Assert.Equal(physical, manifest.Effect);
        Assert.Equal("{\"name\":\"Ada\"}\n", await files.ReadRouteAsync([manifest], EtlOutputRoute.Main));
        var delivery = Assert.IsType<EtlDeliveryLease>(await store.ClaimDeliveryAsync(files.Scope, "inspection-only", deadline.Token));
        Assert.Equal(terminal.TerminalResult, delivery.Result);
        Assert.Equal(invocation.ResultAddress, delivery.Address);
        // No native parent exists here; inspection does not pretend this owed delivery was consumed.
    }
}
