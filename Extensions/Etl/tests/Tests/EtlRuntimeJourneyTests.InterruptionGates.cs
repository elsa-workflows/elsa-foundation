using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Etl.Tests;

public sealed partial class EtlRuntimeJourneyTests
{
    [Fact]
    public async Task Actual_worker_interrupted_before_reservation_rebuilds_the_same_descriptor_without_owed_quota()
    {
        await using var files = new RuntimeFiles("id,name\n1,Ada\n2,Bram\n");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var (host, invocation) = await files.PrepareWorkerRunAsync(EtlTestPipelines.Create() with
        {
            Limits = new EtlLimits { PartitionCount = 1 }
        }, "before-reservation", deadline.Token);
        await using var operation = host.Services.CreateAsyncScope();
        var store = operation.ServiceProvider.GetRequiredService<IEtlRuntimeStore>();

        var predecessor = await files.StartWorkerAsync("before-reservation", deadline.Token,
            storeGate: EtlWorkerStoreGate.BeforeReservation);
        var interrupted = await predecessor.WaitForReservationAsync(deadline.Token);
        Assert.Equal(predecessor.ProcessId, interrupted.ProcessId);
        Assert.NotEqual(Environment.ProcessId, interrupted.ProcessId);
        Assert.Equal(0, interrupted.Lease.PartitionOrdinal);
        Assert.Equal(interrupted.Lease.NextOrdinal, interrupted.Batch.StartOrdinal);
        Assert.Equal(2, interrupted.Batch.EndOrdinalExclusive - interrupted.Batch.StartOrdinal);

        var runBeforeReservation = Assert.IsType<EtlRunView>(await store.FindRunAsync(files.Scope, invocation.RunId, deadline.Token));
        var partitionBeforeReservation = Assert.Single(await store.ListPartitionsAsync(files.Scope, invocation.RunId, -1, 1, deadline.Token));
        Assert.Equal(EtlRunStatus.Running, runBeforeReservation.Status);
        Assert.Null(runBeforeReservation.TerminalResult);
        Assert.Equal(0, runBeforeReservation.OutstandingRecords);
        Assert.Equal(0, runBeforeReservation.OutstandingBytes);
        Assert.Equal(interrupted.Lease.Owner, partitionBeforeReservation.Owner);
        Assert.Equal(interrupted.Lease.Fence, partitionBeforeReservation.Fence);
        Assert.Equal(interrupted.Lease.NextOrdinal, partitionBeforeReservation.NextOrdinal);
        Assert.Null(partitionBeforeReservation.OwedBatch);
        Assert.Empty(await store.ListManifestAsync(files.Scope, invocation.RunId, null, 10, deadline.Token));
        Assert.False(Directory.Exists(files.OutputRoot));

        await predecessor.CrashAsync();
        Assert.True(predecessor.HasExited);
        var survivor = await files.StartWorkerAsync("reservation-survivor", deadline.Token);
        var retried = await survivor.WaitForAcceptanceAsync(deadline.Token);
        Assert.Equal(survivor.ProcessId, retried.ProcessId);
        Assert.True(retried.Lease.Fence > interrupted.Lease.Fence);
        Assert.NotEqual(interrupted.Lease.Owner, retried.Lease.Owner);
        Assert.Equal(interrupted.Lease.NextOrdinal, retried.Lease.NextOrdinal);
        Assert.Equal(interrupted.Batch, retried.Acceptance.Batch);
        Assert.Equal(2, retried.Acceptance.AcceptedRecords);

        var retriedRun = Assert.IsType<EtlRunView>(await store.FindRunAsync(files.Scope, invocation.RunId, deadline.Token));
        var owed = Assert.Single(await store.ListPartitionsAsync(files.Scope, invocation.RunId, -1, 1, deadline.Token)).OwedBatch;
        Assert.NotNull(owed);
        Assert.Equal(interrupted.Batch, owed.Batch);
        Assert.Equal(2, retriedRun.OutstandingRecords);
        Assert.True(retriedRun.OutstandingBytes > 0);
        Assert.Empty(await store.ListManifestAsync(files.Scope, invocation.RunId, null, 10, deadline.Token));

        await survivor.ReleaseAsync(deadline.Token);
        var terminal = await files.WaitForTerminalAsync(host, invocation.RunId);
        Assert.Equal(EtlRunStatus.Succeeded, terminal.Status);
        Assert.Equal(2, terminal.AcceptedRecords);
        Assert.Equal(0, terminal.OutstandingRecords);
        Assert.Equal(0, terminal.OutstandingBytes);
        var manifest = Assert.Single(await store.ListManifestAsync(files.Scope, invocation.RunId, null, 10, deadline.Token));
        Assert.Equal(retried.Acceptance.Effects.Single(), manifest.Effect);
        Assert.Equal("{\"name\":\"Ada\"}\n{\"name\":\"Bram\"}\n", await files.ReadRouteAsync([manifest], EtlOutputRoute.Main));
    }

    [Fact]
    public async Task Actual_worker_interrupted_after_acceptance_commit_resumes_at_the_next_batch()
    {
        await using var files = new RuntimeFiles("id,name\n1,Ada\n2,Bram\n");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var (host, invocation) = await files.PrepareWorkerRunAsync(EtlTestPipelines.Create() with
        {
            Limits = new EtlLimits { PartitionCount = 1, MaxBatchRecords = 1 }
        }, "after-acceptance-commit", deadline.Token);
        await using var operation = host.Services.CreateAsyncScope();
        var store = operation.ServiceProvider.GetRequiredService<IEtlRuntimeStore>();

        var predecessor = await files.StartWorkerAsync("after-acceptance", deadline.Token,
            storeGate: EtlWorkerStoreGate.AfterAcceptanceCommit);
        var committed = await predecessor.WaitForCommittedAcceptanceAsync(deadline.Token);
        Assert.Equal(predecessor.ProcessId, committed.ProcessId);
        Assert.NotEqual(Environment.ProcessId, committed.ProcessId);
        Assert.Equal(EtlStoreStatus.Applied, committed.Result.Status);
        var committedLease = Assert.IsType<EtlPartitionLease>(committed.Result.Lease);
        Assert.Equal(committed.Acceptance.Batch.StartOrdinal, committed.Lease.NextOrdinal);
        Assert.Equal(committed.Acceptance.Batch.EndOrdinalExclusive, committedLease.NextOrdinal);
        Assert.Equal(1, committed.Acceptance.Batch.EndOrdinalExclusive - committed.Acceptance.Batch.StartOrdinal);
        Assert.Equal(1, committed.Acceptance.AcceptedRecords);

        var runAfterCommit = Assert.IsType<EtlRunView>(await store.FindRunAsync(files.Scope, invocation.RunId, deadline.Token));
        var partitionAfterCommit = Assert.Single(await store.ListPartitionsAsync(files.Scope, invocation.RunId, -1, 1, deadline.Token));
        var committedManifest = Assert.Single(await store.ListManifestAsync(files.Scope, invocation.RunId, null, 10, deadline.Token));
        Assert.Equal(EtlRunStatus.Running, runAfterCommit.Status);
        Assert.Null(runAfterCommit.TerminalResult);
        Assert.Equal(1, runAfterCommit.AcceptedRecords);
        Assert.Equal(0, runAfterCommit.OutstandingRecords);
        Assert.Equal(0, runAfterCommit.OutstandingBytes);
        Assert.Equal(committedLease.NextOrdinal, partitionAfterCommit.NextOrdinal);
        Assert.Null(partitionAfterCommit.OwedBatch);
        Assert.Equal(committed.Acceptance.Batch.BatchId, committedManifest.BatchId);
        Assert.Equal(committed.Acceptance.Effects.Single(), committedManifest.Effect);
        Assert.Equal("{\"name\":\"Ada\"}\n", await files.ReadRouteAsync([committedManifest], EtlOutputRoute.Main));

        await predecessor.CrashAsync();
        Assert.True(predecessor.HasExited);
        var survivor = await files.StartWorkerAsync("next-batch-survivor", deadline.Token);
        var next = await survivor.WaitForAcceptanceAsync(deadline.Token);
        Assert.Equal(survivor.ProcessId, next.ProcessId);
        Assert.True(next.Lease.Fence > committed.Lease.Fence);
        Assert.NotEqual(committed.Lease.Owner, next.Lease.Owner);
        Assert.Equal(committedLease.NextOrdinal, next.Lease.NextOrdinal);
        Assert.Equal(committed.Acceptance.Batch.EndOrdinalExclusive, next.Acceptance.Batch.StartOrdinal);
        Assert.Equal(1, next.Acceptance.Batch.EndOrdinalExclusive - next.Acceptance.Batch.StartOrdinal);
        Assert.Equal(1, next.Acceptance.AcceptedRecords);

        var runBeforeNextAcceptance = Assert.IsType<EtlRunView>(await store.FindRunAsync(files.Scope, invocation.RunId, deadline.Token));
        var partitionBeforeNextAcceptance = Assert.Single(await store.ListPartitionsAsync(files.Scope, invocation.RunId, -1, 1, deadline.Token));
        var manifestBeforeNextAcceptance = await store.ListManifestAsync(files.Scope, invocation.RunId, null, 10, deadline.Token);
        Assert.Equal(EtlRunStatus.Running, runBeforeNextAcceptance.Status);
        Assert.Null(runBeforeNextAcceptance.TerminalResult);
        Assert.Equal(1, runBeforeNextAcceptance.AcceptedRecords);
        Assert.Equal(1, runBeforeNextAcceptance.OutstandingRecords);
        Assert.True(runBeforeNextAcceptance.OutstandingBytes > 0);
        Assert.Equal(next.Lease.NextOrdinal, partitionBeforeNextAcceptance.NextOrdinal);
        var nextOwed = Assert.IsType<EtlBatchReservation>(partitionBeforeNextAcceptance.OwedBatch);
        Assert.Equal(next.Acceptance.Batch, nextOwed.Batch);
        Assert.Equal(committedManifest, Assert.Single(manifestBeforeNextAcceptance));

        await survivor.ReleaseAsync(deadline.Token);
        var terminal = await files.WaitForTerminalAsync(host, invocation.RunId);
        Assert.Equal(EtlRunStatus.Succeeded, terminal.Status);
        Assert.Equal(2, terminal.AcceptedRecords);
        Assert.Equal(0, terminal.OutstandingRecords);
        Assert.Equal(0, terminal.OutstandingBytes);
        var manifest = await store.ListManifestAsync(files.Scope, invocation.RunId, null, 10, deadline.Token);
        Assert.Equal(2, manifest.Count);
        Assert.Equal(new[] { committed.Acceptance.Batch.StartOrdinal, next.Acceptance.Batch.StartOrdinal },
            manifest.Select(entry => entry.Position.BatchStartOrdinal));
        Assert.Equal(committed.Acceptance.Effects.Single(), manifest[0].Effect);
        Assert.Equal(next.Acceptance.Effects.Single(), manifest[1].Effect);
        Assert.Equal("{\"name\":\"Ada\"}\n{\"name\":\"Bram\"}\n", await files.ReadRouteAsync(manifest, EtlOutputRoute.Main));
    }
}
