using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Persistence.EntityFrameworkCore.Stores;
using Elsa.Etl.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Etl.Tests;

public sealed partial class EtlRuntimeJourneyTests
{
    [Fact]
    public async Task Two_real_worker_processes_hold_distinct_nonempty_partitions_and_second_recovers_after_predecessor_exit()
    {
        await using var files = new RuntimeFiles("id,name\n1,Ada\n2,Bram\n3,Cora\n1,Daan\n");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var pipeline = EtlTestPipelines.Create() with
        {
            Limits = EtlTestPipelines.Create().Limits with { PartitionCount = 2, MaxBatchRecords = 2 }
        };
        var (seed, invocation) = await files.PrepareWorkerRunAsync(pipeline, "two-partition-process-recovery", deadline.Token);
        await using var operation = seed.Services.CreateAsyncScope();
        var store = operation.ServiceProvider.GetRequiredService<IEtlRuntimeStore>();
        var preparedPartitions = await store.ListPartitionsAsync(files.Scope, invocation.RunId, -1, 10, deadline.Token);
        Assert.Equal(2, preparedPartitions.Count);
        Assert.All(preparedPartitions, partition => Assert.True(partition.EndOrdinalExclusive > partition.StartOrdinal));

        var predecessor = await files.StartWorkerAsync("partition-predecessor", deadline.Token,
            storeGate: EtlWorkerStoreGate.BeforeAcceptance);
        var first = await predecessor.WaitForAcceptanceAsync(deadline.Token);
        Assert.Equal(predecessor.ProcessId, first.ProcessId);
        Assert.NotEqual(Environment.ProcessId, first.ProcessId);

        var survivor = await files.StartWorkerAsync("partition-survivor", deadline.Token,
            storeGate: EtlWorkerStoreGate.BeforeAcceptance);
        var second = await survivor.WaitForAcceptanceAsync(deadline.Token);
        Assert.Equal(survivor.ProcessId, second.ProcessId);
        Assert.NotEqual(first.ProcessId, second.ProcessId);
        Assert.NotEqual(first.Lease.PartitionOrdinal, second.Lease.PartitionOrdinal);
        Assert.Equal(new[] { 0, 1 }, new[] { first.Lease.PartitionOrdinal, second.Lease.PartitionOrdinal }.OrderBy(ordinal => ordinal));
        Assert.All(new[] { first, second }, accepted =>
        {
            Assert.Equal(2, accepted.Acceptance.AcceptedRecords);
            Assert.Equal(accepted.Lease.NextOrdinal, accepted.Acceptance.Batch.StartOrdinal);
            Assert.True(accepted.Acceptance.Batch.EndOrdinalExclusive > accepted.Acceptance.Batch.StartOrdinal);
            Assert.True(accepted.Acceptance.Batch.EndOrdinalExclusive <= accepted.Lease.EndOrdinalExclusive);
            Assert.Equal(EtlOutputRoute.Main, Assert.Single(accepted.Acceptance.Effects).Route);
        });

        // Both processes have completed real read/transform/publication work but are paused before
        // the acceptance transaction. Their distinct leases and reservations remain live together.
        var heldPartitions = await store.ListPartitionsAsync(files.Scope, invocation.RunId, -1, 10, deadline.Token);
        foreach (var attempt in new[] { first, second })
        {
            var held = heldPartitions[attempt.Lease.PartitionOrdinal];
            Assert.Equal(attempt.Lease.Owner, held.Owner);
            Assert.Equal(attempt.Lease.Fence, held.Fence);
            Assert.Equal(held.StartOrdinal, held.NextOrdinal);
            Assert.Equal(attempt.Acceptance.Batch,
                Assert.IsType<EtlBatchReservation>(held.OwedBatch).Batch);
            var effect = Assert.Single(attempt.Acceptance.Effects);
            Assert.Equal(EtlOutputRoute.Main, effect.Route);
            Assert.Equal(2, effect.RecordCount);
            Assert.True(File.Exists(Path.Combine(files.OutputRoot, effect.Chunk.Key)));
        }
        var running = Assert.IsType<EtlRunView>(await store.FindRunAsync(files.Scope, invocation.RunId, deadline.Token));
        Assert.Equal(4, running.OutstandingRecords);
        Assert.Equal(first.Acceptance.Batch.InputBytes + second.Acceptance.Batch.InputBytes, running.OutstandingBytes);
        Assert.Equal(0, running.AcceptedRecords);
        Assert.Empty(await store.ListManifestAsync(files.Scope, invocation.RunId, null, 10, deadline.Token));
        Assert.Null(await files.ReadOutboxAsync(seed.Services, invocation.RunId));

        await predecessor.CrashAsync();
        Assert.True(predecessor.HasExited);
        await survivor.ReleaseAsync(deadline.Token);

        var terminal = await files.WaitForTerminalAsync(seed, invocation.RunId);
        Assert.Equal(EtlRunStatus.Succeeded, terminal.Status);
        Assert.Equal(4, terminal.AcceptedRecords);
        Assert.Equal(0, terminal.OutstandingRecords);
        Assert.Equal(0, terminal.OutstandingBytes);
        Assert.NotNull(terminal.TerminalResult);

        // The final acceptance clears the durable lease owner in the same transaction as terminal
        // settlement, so use the post-commit fixture receipt to prove which worker took it over.
        var recoveredAcceptance = await survivor.WaitForCommittedAcceptanceReceiptAsync(2, deadline.Token);
        Assert.Equal(survivor.ProcessId, recoveredAcceptance.ProcessId);
        Assert.Equal(EtlStoreStatus.Applied, recoveredAcceptance.Result.Status);
        Assert.Equal(first.Lease.PartitionOrdinal, recoveredAcceptance.Lease.PartitionOrdinal);
        Assert.Equal(second.Lease.Owner, recoveredAcceptance.Lease.Owner);
        Assert.True(recoveredAcceptance.Lease.Fence > first.Lease.Fence);
        Assert.Equal(first.Acceptance.Batch, recoveredAcceptance.Acceptance.Batch);
        Assert.Equal(first.Acceptance.AcceptedRecords, recoveredAcceptance.Acceptance.AcceptedRecords);
        Assert.Equal(first.Acceptance.Effects, recoveredAcceptance.Acceptance.Effects);

        var recoveredPartition = await store.ListPartitionsAsync(files.Scope, invocation.RunId, -1, 10, deadline.Token);
        var oldLease = first.Lease;
        Assert.Equal(2, recoveredPartition.Count);
        var recovered = recoveredPartition[oldLease.PartitionOrdinal];
        Assert.True(recovered.Fence > oldLease.Fence);
        Assert.Null(recovered.Owner);
        Assert.Equal(recovered.EndOrdinalExclusive, recovered.NextOrdinal);
        var settledPartition = recoveredPartition.Single(partition => partition.PartitionOrdinal != oldLease.PartitionOrdinal);
        Assert.Equal(settledPartition.EndOrdinalExclusive, settledPartition.NextOrdinal);

        var manifest = await store.ListManifestAsync(files.Scope, invocation.RunId, null, 10, deadline.Token);
        Assert.Equal(2, manifest.Count);
        Assert.All(manifest, entry => Assert.Equal(2, entry.Effect.RecordCount));
        Assert.Equal("{\"name\":\"Ada\"}\n{\"name\":\"Bram\"}\n{\"name\":\"Cora\"}\n{\"name\":\"Daan\"}\n",
            await files.ReadRouteAsync(manifest, EtlOutputRoute.Main));
    }
}
