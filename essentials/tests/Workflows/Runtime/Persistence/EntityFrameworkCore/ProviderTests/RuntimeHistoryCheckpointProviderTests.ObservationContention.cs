using System.Data;
using System.Data.Common;
using System.Diagnostics;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.ProviderTests;

internal static partial class RuntimeHistoryCheckpointProviderContract
{
    // The participant is real, but its eventual public owner still supplies source authorization,
    // acknowledgement handling and DI. This control proves only competing SQL publication.
    private static async Task ConcurrentObservation(Database database, string engine)
    {
        await using (var seed = database.Open()) await database.Store(seed).CommitAsync(Captured("original"), Immediate);
        await using var observation = database.Open(); await observation.Database.OpenConnectionAsync();
        var observerId = await ProcessId(observation, engine);
        await using var transaction = await observation.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted);
        var delivery = new RuntimeHistoryObservation(Policy,
            new("delivery-contention", RunId, 0, RuntimeHistoryEventCategory.DeliveryObservation, "DeliveryReceived", At, "activity")
            { Authority = RuntimeHistoryAuthority.DeliveryObservation, OccurredAtKnown = false, BookmarkId = "bookmark", Causation = new(DeliveryId: "delivery") });
        var staged = await EfRuntimeHistoryObservation.StageAsync(observation, new Access(), delivery, Scope,
            database.Clock.GetUtcNow(), RuntimeHistoryEfModule.Chain, CancellationToken.None);
        Assert.True(staged.Recorded); Assert.Equal(3, staged.Event!.Sequence);
        await observation.SaveChangesAsync();

        await using var native = database.Open(); await native.Database.OpenConnectionAsync();
        var nativeId = await ProcessId(native, engine);
        var next = Plain("next") with
        {
            StateChanges = new(null, new(RunId, RuntimeStateChangeOperation.Upsert, new SchedulerState(RunId, 2), new Dictionary<string, string>()), [], [], [], [], []),
            HistoryBatches = [new(Policy, "checkpoint-next", At, false,
                [new(new("next-started", RunId, 0, RuntimeHistoryEventCategory.ActivityLifecycle, "ActivityStarted", At, "next-activity", checkpointId: "checkpoint-next")
                    { Authority = RuntimeHistoryAuthority.CommittedTransition, ActivityAncestryKnown = true }, 0)])]
        };
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var writing = database.Store(native).CommitAsync(next, Immediate, timeout.Token).AsTask();
        var released = false;
        try
        {
            await using var monitor = database.Open();
            await WaitForBlockingSession(monitor, engine, nativeId, observerId, writing, timeout.Token);
            await transaction.CommitAsync(timeout.Token); released = true;
            await writing.WaitAsync(timeout.Token);
        }
        finally
        {
            if (!released)
            {
                await transaction.RollbackAsync();
                // Settle the competing operation after releasing the owned lock, while preserving
                // the original failed lock-observation assertion if it was the primary failure.
                try { await writing; } catch { }
            }
        }
        Assert.False(native.ChangeTracker.HasChanges()); Assert.Null(native.Database.CurrentTransaction);
        await using var read = database.Open(); var mapping = new EfRuntimeHistoryProjectionMapping(RuntimeHistoryEfModule.Chain);
        var head = mapping.Read(await read.Set<RuntimeHistoryHeadEntity>().SingleAsync(), Scope, RunId);
        Assert.Equal(4, head.LastSequence); Assert.Equal("next", head.LastPhysicalCommitId);
        var rows = await read.Set<RuntimeHistoryEventEntity>().OrderBy(row => row.Sequence).ToArrayAsync();
        Assert.Equal(new long[] { 1, 2, 3, 4 }, rows.Select(row => row.Sequence));
        var events = rows.Select(row => mapping.Read(row, Scope, RunId, head)).ToArray();
        Assert.Equal(new[] { "started", "created", "delivery-contention", "next-started" }, events.Select(item => item.HistoryEventId));
        Assert.Equal("owner-observation:delivery-contention", events[2].PhysicalCommitId);
        Assert.Equal("next", events[3].PhysicalCommitId);
        Assert.Equal(2, await read.RuntimeCheckpointCommits.CountAsync()); Assert.Single(await read.Bookmarks.ToArrayAsync());
        Assert.Single(await read.SchedulerStates.ToArrayAsync());
        Assert.Equal(2, await read.Set<RuntimeHistoryOccurrenceEntity>().CountAsync());
        var counter = await read.Set<RuntimeHistoryOccurrenceCounterEntity>().SingleAsync(row => row.ActivityScopeId == null);
        Assert.Equal(2, counter.NextOrdinal); Assert.Equal(4, counter.CoveredThroughSequence);
    }

    private static async Task<long> ProcessId(RuntimeDbContext context, string engine)
    {
        await using var command = context.Database.GetDbConnection().CreateCommand();
        command.CommandText = engine switch { "PostgreSql" => "SELECT pg_backend_pid()", "SqlServer" => "SELECT @@SPID", _ => "SELECT CONNECTION_ID()" };
        command.Transaction = context.Database.CurrentTransaction?.GetDbTransaction();
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private static async Task WaitForBlockingSession(RuntimeDbContext monitor, string engine, long waiting, long blocking, Task writing, CancellationToken token)
    {
        await monitor.Database.OpenConnectionAsync(token);
        await using var command = monitor.Database.GetDbConnection().CreateCommand();
        command.CommandText = engine switch
        {
            "PostgreSql" => "SELECT COUNT(*) FROM pg_stat_activity WHERE pid = CAST(@waiting AS integer) AND CAST(@blocking AS integer) = ANY(pg_blocking_pids(pid))",
            "SqlServer" => "SELECT COUNT(*) FROM sys.dm_exec_requests WHERE session_id = @waiting AND blocking_session_id = @blocking",
            _ => "SELECT COUNT(*) FROM performance_schema.data_lock_waits w JOIN performance_schema.threads r ON r.THREAD_ID = w.REQUESTING_THREAD_ID JOIN performance_schema.threads b ON b.THREAD_ID = w.BLOCKING_THREAD_ID WHERE r.PROCESSLIST_ID = @waiting AND b.PROCESSLIST_ID = @blocking"
        };
        var reader = command.CreateParameter(); reader.ParameterName = "waiting"; reader.Value = waiting; command.Parameters.Add(reader);
        var owner = command.CreateParameter(); owner.ParameterName = "blocking"; owner.Value = blocking; command.Parameters.Add(owner);
        var deadline = Stopwatch.StartNew();
        while (deadline.Elapsed < TimeSpan.FromSeconds(60))
        {
            token.ThrowIfCancellationRequested();
            if (Convert.ToInt64(await command.ExecuteScalarAsync(token)) > 0) return;
            if (writing.IsCompleted)
            {
                await writing;
                Assert.Fail("The native checkpoint completed before the conflicting observation transaction released its lock.");
            }
            await Task.Delay(25, token);
        }
        Assert.Fail("The provider did not report the actual observation session blocking the native checkpoint session.");
    }
}
