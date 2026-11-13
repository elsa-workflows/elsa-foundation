using System.Data.Common;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Recovery;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class EfRuntimeHistoryCheckpointTests
{
    private static readonly IRuntimeRecoveryContinuationCodec MutationCodec = new HmacRuntimeRecoveryContinuationCodec(
        Options.Create(new RuntimeRecoveryContinuationOptions { SigningKey = "mutation-owner-controls-0123456789-shared", AllowEphemeralDevelopmentKey = false }));

    [Fact]
    public async Task Independent_owner_commits_once_replays_original_receipt_and_keeps_native_coverage()
    {
        await using var db = await CapturedDatabaseAsync(); await using var context = db.Open(); var owner = MutationOwner(context);
        var first = await owner.RecordAsync(Observation("independent")); var replay = await owner.RecordAsync(Observation("independent"));
        Assert.True(first.Recorded); Assert.True(replay.Recorded); Assert.Equal(2, first.Event!.Sequence);
        Assert.NotNull(replay.Event); Assert.Equal(first.Event.HistoryEventId, replay.Event.HistoryEventId);
        Assert.Equal(first.Event.Sequence, replay.Event.Sequence); Assert.Equal(first.Event.PhysicalCommitId, replay.Event.PhysicalCommitId);
        Assert.Equal(first.Event.RecordedAt, replay.Event.RecordedAt); Assert.Equal(first.Event.OccurredAt, replay.Event.OccurredAt);
        Assert.Equal(RuntimeHistoryIdentity.Compute(first.Event), RuntimeHistoryIdentity.Compute(replay.Event));
        Assert.Empty(context.ChangeTracker.Entries()); Assert.Null(context.Database.CurrentTransaction);
        await using var read = db.Open(); var head = await read.Set<RuntimeHistoryHeadEntity>().SingleAsync();
        Assert.Equal(2, head.LastSequence); Assert.Equal("native-checkpoint", head.LastPhysicalCommitId);
        Assert.Equal(Now.AddDays(1), head.LastCommittedAt); Assert.Equal(2, await read.Set<RuntimeHistoryEventEntity>().CountAsync());
        Assert.Single(await read.Set<RuntimeHistoryOccurrenceEntity>().ToArrayAsync());
        Assert.Equal(2, (await read.Set<RuntimeHistoryOccurrenceCounterEntity>().SingleAsync(row => row.ActivityScopeId == null)).CoveredThroughSequence);
    }

    [Fact]
    public async Task Owner_purge_keeps_replay_receipts_and_cannot_resurrect_retired_content()
    {
        await using var db = await CapturedDatabaseAsync(); await using var context = db.Open(); var owner = MutationOwner(context);
        await owner.RecordAsync(Observation("retired")); Assert.Equal(2, await owner.PurgeThroughAsync(Run, long.MaxValue));
        var replay = await owner.RecordAsync(Observation("retired")); Assert.True(replay.Recorded); Assert.Null(replay.Event);
        Assert.Equal(2, await owner.PurgeThroughAsync(Run, 0));
        var next = await PersistAsync(db, Commit("later-native", [Batch("later-native", Candidate("later-native", "later-native-event", 0))]));
        Assert.Equal(3, Assert.Single(next).Sequence);
        await using var read = db.Open(); Assert.Equal(3, await read.Set<RuntimeHistoryEventEntity>().CountAsync());
        Assert.Equal(2, await read.Set<RuntimeHistoryEventEntity>().CountAsync(row => row.IsPurged && row.ContentJson == ""));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Lost_commit_acknowledgement_reads_actual_durable_observation_or_purge_receipt(bool purge)
    {
        await using var db = await CapturedDatabaseAsync(); var loss = new MutationAckLoss();
        await using var context = db.Open(loss); var owner = MutationOwner(context);
        if (purge) Assert.Equal(1, await owner.PurgeThroughAsync(Run, long.MaxValue));
        else { var result = await owner.RecordAsync(Observation("lost-ack")); Assert.True(result.Recorded); Assert.Equal(2, result.Event!.Sequence); }
        Assert.Equal(1, loss.Hits); Assert.Empty(context.ChangeTracker.Entries()); Assert.Null(context.Database.CurrentTransaction);
        await using var read = db.Open(); var head = await read.Set<RuntimeHistoryHeadEntity>().SingleAsync();
        Assert.Equal(purge ? 1 : 2, head.LastSequence); Assert.Equal(purge ? 1 : 0, head.PurgedThroughSequence);
    }

    [Fact]
    public async Task Lost_acknowledgement_of_purged_observation_replay_returns_recorded_without_content()
    {
        await using var db = await CapturedDatabaseAsync(); await using (var setup = db.Open())
        { var owner = MutationOwner(setup); await owner.RecordAsync(Observation("purged-ack")); await owner.PurgeThroughAsync(Run, 2); }
        var loss = new MutationAckLoss(); await using var context = db.Open(loss);
        var result = await MutationOwner(context).RecordAsync(Observation("purged-ack"));
        Assert.True(result.Recorded); Assert.Null(result.Event); Assert.Equal(1, loss.Hits);
        await using var read = db.Open(); Assert.Equal(2, (await read.Set<RuntimeHistoryHeadEntity>().SingleAsync()).LastSequence);
        Assert.All(await read.Set<RuntimeHistoryEventEntity>().ToArrayAsync(), row => Assert.True(row.IsPurged));
    }

    [Fact]
    public async Task Purge_ack_reconciliation_does_not_broaden_a_max_request_to_later_arrivals()
    {
        await using var db = await CapturedDatabaseAsync();
        var loss = new MutationAckLoss(async () =>
        { await PersistAsync(db, Commit("after-purge", [Batch("after-purge", Candidate("after-purge", "arrived-later", 0))])); });
        await using var context = db.Open(loss); Assert.Equal(1, await MutationOwner(context).PurgeThroughAsync(Run, long.MaxValue));
        await using var read = db.Open(); var head = await read.Set<RuntimeHistoryHeadEntity>().SingleAsync();
        Assert.Equal(1, head.PurgedThroughSequence); Assert.Equal(2, head.LastSequence);
        var later = await read.Set<RuntimeHistoryEventEntity>().SingleAsync(row => row.Sequence == 2);
        Assert.False(later.IsPurged); Assert.NotEmpty(later.ContentJson); Assert.Equal(1, loss.Hits);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Commit_refusal_never_acknowledges_or_automatically_reapplies_a_staged_mutation(bool purge)
    {
        await using var db = await CapturedDatabaseAsync(); var refusal = new MutationCommitRefusal();
        await using var context = db.Open(refusal); var owner = MutationOwner(context);
        var failure = purge
            ? await Assert.ThrowsAsync<RuntimeHistoryUnavailableException>(() => owner.PurgeThroughAsync(Run, long.MaxValue).AsTask())
            : await Assert.ThrowsAsync<RuntimeHistoryUnavailableException>(() => owner.RecordAsync(Observation("refused")).AsTask());
        Assert.IsType<IOException>(failure.InnerException); Assert.Equal(1, refusal.Hits);
        Assert.Empty(context.ChangeTracker.Entries()); Assert.Null(context.Database.CurrentTransaction);
        await using var read = db.Open(); var head = await read.Set<RuntimeHistoryHeadEntity>().SingleAsync();
        Assert.Equal(1, head.LastSequence); Assert.Equal(0, head.PurgedThroughSequence);
        Assert.False((await read.Set<RuntimeHistoryEventEntity>().SingleAsync()).IsPurged);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Real_sql_write_failure_rolls_back_head_and_content_and_cleans_the_owner(bool purge)
    {
        await using var db = await CapturedDatabaseAsync(); await using var context = db.Open(); var owner = MutationOwner(context);
        await context.Database.ExecuteSqlRawAsync(purge
            ? "CREATE TRIGGER reject_mutation BEFORE UPDATE ON elsa_runtime_history_event BEGIN SELECT RAISE(ABORT, 'purge refused'); END;"
            : "CREATE TRIGGER reject_mutation BEFORE INSERT ON elsa_runtime_history_event BEGIN SELECT RAISE(ABORT, 'observation refused'); END;");
        if (purge) await Assert.ThrowsAsync<RuntimeHistoryUnavailableException>(() => owner.PurgeThroughAsync(Run, 1).AsTask());
        else await Assert.ThrowsAsync<RuntimeHistoryUnavailableException>(() => owner.RecordAsync(Observation("retry")).AsTask());
        Assert.Empty(context.ChangeTracker.Entries()); Assert.Null(context.Database.CurrentTransaction);
        await using (var read = db.Open())
        { var head = await read.Set<RuntimeHistoryHeadEntity>().SingleAsync(); Assert.Equal(1, head.LastSequence); Assert.Equal(0, head.PurgedThroughSequence); }
        await context.Database.ExecuteSqlRawAsync("DROP TRIGGER reject_mutation;");
        if (purge) Assert.Equal(1, await owner.PurgeThroughAsync(Run, 1));
        else Assert.Equal(2, (await owner.RecordAsync(Observation("retry"))).Event!.Sequence);
    }

    [Fact]
    public async Task Failed_ack_reconciliation_preserves_the_original_write_and_lookup_causes()
    {
        await using var db = await CapturedDatabaseAsync();
        var loss = new MutationAckLoss(async () => { await using var alter = db.Open(); await alter.Database.ExecuteSqlRawAsync("DROP TABLE elsa_runtime_history_head;"); });
        await using var context = db.Open(loss);
        var failure = await Assert.ThrowsAsync<RuntimeHistoryUnavailableException>(() => MutationOwner(context).RecordAsync(Observation("unreadable-ack")).AsTask());
        var causes = Assert.IsType<AggregateException>(failure.InnerException).Flatten().InnerExceptions;
        Assert.Contains(causes, cause => cause is IOException && cause.Message == "Actual mutation commit acknowledgement lost.");
        Assert.Contains(causes, cause => cause is RuntimeHistoryUnavailableException { InnerException: SqliteException });
        Assert.Empty(context.ChangeTracker.Entries()); Assert.Null(context.Database.CurrentTransaction);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Missing_native_or_uncaptured_history_cannot_be_created_by_independent_owner(bool missingNative)
    {
        await using var db = await Database.CreateAsync(explicitLegacy: true); await using var context = db.Open();
        if (missingNative) await context.WorkflowExecutionStates.ExecuteDeleteAsync();
        var owner = MutationOwner(context); Assert.False((await owner.RecordAsync(Observation("no-owner"))).Recorded);
        Assert.Equal(0, await owner.PurgeThroughAsync(Run, long.MaxValue)); Assert.Empty(await context.Set<RuntimeHistoryHeadEntity>().ToArrayAsync());
    }

    [Fact]
    public async Task Mutation_owner_requires_its_actual_reader_access_and_chain()
    {
        await using var db = await CapturedDatabaseAsync(); await using var context = db.Open(); var access = new Accessor();
        var selected = new EfRuntimeHistoryStore(context, access, MutationCodec, Chain);
        var owner = new EfRuntimeHistoryMutationStore(context, access, selected, TimeProvider.System, Chain);
        Assert.True(owner.CanRecordHistory(selected)); Assert.False(owner.CanRecordHistory(new EfRuntimeHistoryStore(context, access, MutationCodec, Chain)));
        var foreign = new EfRuntimeHistoryStore(context, new Accessor(), MutationCodec, Chain);
        var invalid = new EfRuntimeHistoryMutationStore(context, access, foreign, TimeProvider.System, Chain);
        Assert.False(invalid.CanRecordHistory(foreign));
        await Assert.ThrowsAsync<InvalidOperationException>(() => invalid.RecordAsync(Observation("wrong-owner")).AsTask());
    }

    [Theory]
    [InlineData("dirty")] [InlineData("transaction")] [InlineData("ambient")] [InlineData("cancelled")]
    public async Task Owner_refuses_caller_work_transactions_and_requested_cancellation(string boundary)
    {
        await using var db = await CapturedDatabaseAsync(); await using var context = db.Open(); var owner = MutationOwner(context);
        using var ambient = boundary == "ambient" ? new System.Transactions.TransactionScope(System.Transactions.TransactionScopeAsyncFlowOption.Enabled) : null;
        await using var caller = boundary == "transaction" ? await context.Database.BeginTransactionAsync() : null;
        if (boundary == "dirty") (await context.Set<RuntimeHistoryHeadEntity>().SingleAsync()).ContentJson = "pending caller change";
        if (boundary == "cancelled")
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => owner.RecordAsync(Observation("cancelled"), new CancellationToken(true)).AsTask());
        else await Assert.ThrowsAsync<InvalidOperationException>(() => owner.RecordAsync(Observation("refused-boundary")).AsTask());
        Assert.Same(caller, context.Database.CurrentTransaction);
        Assert.Equal(boundary == "dirty", context.ChangeTracker.HasChanges());
    }

    [Fact]
    public async Task Retired_access_and_conflicting_semantic_replay_remain_hard_refusals()
    {
        await using var db = await CapturedDatabaseAsync();
        await using (var context = db.Open())
        {
            var owner = MutationOwner(context); await owner.RecordAsync(Observation("fixed"));
            await Assert.ThrowsAsync<RuntimeHistoryIntegrityException>(() => owner.RecordAsync(Observation("fixed", Now.AddMinutes(1))).AsTask());
            Assert.Empty(context.ChangeTracker.Entries());
        }
        var access = new Accessor(); await using var retired = db.Open(new RetireAccess(access));
        await Assert.ThrowsAsync<InvalidOperationException>(() => MutationOwner(retired, access).RecordAsync(Observation("retired-access")).AsTask());
        Assert.Empty(retired.ChangeTracker.Entries()); Assert.Null(retired.Database.CurrentTransaction);
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public async Task Accepted_unrelated_native_tracking_survives_success_and_failure_and_can_still_be_saved(bool purge, bool fail)
    {
        await using var db = await CapturedDatabaseAsync();
        await using (var seed = db.Open())
        {
            seed.WorkflowExecutionStates.Add(EfWorkflowExecutionStateStore.ToEntity(Workflow(null) with { WorkflowExecutionId = "unrelated" },
                Scope, EfWorkflowExecutionStateStore.CreateId(Scope, "unrelated"), 1));
            await seed.SaveChangesAsync();
        }
        await using var context = fail ? db.Open(new MutationCommitRefusal()) : db.Open();
        var id = EfWorkflowExecutionStateStore.CreateId(Scope, "unrelated");
        var callerRow = await context.WorkflowExecutionStates.SingleAsync(row => row.Id == id); var owner = MutationOwner(context);
        if (fail)
        {
            if (purge) await Assert.ThrowsAsync<RuntimeHistoryUnavailableException>(() => owner.PurgeThroughAsync(Run, 1).AsTask());
            else await Assert.ThrowsAsync<RuntimeHistoryUnavailableException>(() => owner.RecordAsync(Observation("caller-state")).AsTask());
        }
        else if (purge) Assert.Equal(1, await owner.PurgeThroughAsync(Run, 1));
        else Assert.True((await owner.RecordAsync(Observation("caller-state"))).Recorded);
        Assert.Equal(EntityState.Unchanged, context.Entry(callerRow).State);
        callerRow.Revision++; await context.SaveChangesAsync();
        await using var read = db.Open(); Assert.Equal(2, (await read.WorkflowExecutionStates.SingleAsync(row => row.Id == id)).Revision);
        Assert.Null(context.Database.CurrentTransaction);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Owner_refreshes_its_clean_tracked_history_after_another_context_appends(bool purge)
    {
        await using var db = await CapturedDatabaseAsync(); await using var stale = db.Open();
        var previous = await stale.Set<RuntimeHistoryHeadEntity>().SingleAsync(); Assert.Equal(1, previous.LastSequence);
        await using (var other = db.Open()) await MutationOwner(other).RecordAsync(Observation("arrived-before-mutation"));
        if (purge) Assert.Equal(2, await MutationOwner(stale).PurgeThroughAsync(Run, long.MaxValue));
        else Assert.Equal(3, (await MutationOwner(stale).RecordAsync(Observation("after-refresh"))).Event!.Sequence);
        Assert.Equal(EntityState.Detached, stale.Entry(previous).State);
        await using var read = db.Open(); Assert.Equal(purge ? 2 : 3, (await read.Set<RuntimeHistoryHeadEntity>().SingleAsync()).LastSequence);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Actual_head_cas_refusal_is_typed_unavailability_and_rolls_back_before_caller_retry(bool purge)
    {
        await using var db = await CapturedDatabaseAsync(); var bump = new BumpMutationHeadRevision(); await using var context = db.Open(bump);
        var owner = MutationOwner(context);
        var failure = purge
            ? await Assert.ThrowsAsync<RuntimeHistoryUnavailableException>(() => owner.PurgeThroughAsync(Run, 1).AsTask())
            : await Assert.ThrowsAsync<RuntimeHistoryUnavailableException>(() => owner.RecordAsync(Observation("cas-refusal")).AsTask());
        Assert.IsType<EfRuntimeHistoryCheckpoint.HeadAllocationConflictException>(failure.InnerException); Assert.Equal(1, bump.Hits);
        Assert.Empty(context.ChangeTracker.Entries()); Assert.Null(context.Database.CurrentTransaction);
        await using (var read = db.Open())
        { var head = await read.Set<RuntimeHistoryHeadEntity>().SingleAsync(); Assert.Equal(1, head.LastSequence); Assert.Equal(0, head.PurgedThroughSequence); }
        if (purge) Assert.Equal(1, await owner.PurgeThroughAsync(Run, 1));
        else Assert.Equal(2, (await owner.RecordAsync(Observation("cas-refusal"))).Event!.Sequence);
    }

    private static EfRuntimeHistoryMutationStore MutationOwner(RuntimeDbContext context, Accessor? selectedAccess = null)
    {
        var access = selectedAccess ?? new Accessor(); var reader = new EfRuntimeHistoryStore(context, access, MutationCodec, Chain);
        return new(context, access, reader, TimeProvider.System, Chain);
    }

    private sealed class MutationAckLoss(Func<Task>? afterCommit = null) : DbTransactionInterceptor
    {
        public int Hits { get; private set; }
        public override async Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (Hits != 0) return;
            Hits++;
            if (afterCommit is not null) await afterCommit();
            throw new IOException("Actual mutation commit acknowledgement lost.");
        }
    }

    private sealed class MutationCommitRefusal : DbTransactionInterceptor
    {
        public int Hits { get; private set; }
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction, TransactionEventData eventData,
            InterceptionResult result, CancellationToken cancellationToken = default)
        {
            if (Hits != 0) return ValueTask.FromResult(result);
            Hits++; throw new IOException("Actual mutation commit refused.");
        }
    }

    // Mechanism control: change the actual stored CAS revision in the same provider transaction
    // immediately before its conditional update. It is not a concurrent-provider contention proof.
    private sealed class BumpMutationHeadRevision : DbCommandInterceptor
    {
        public int Hits { get; private set; }
        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Hits != 0 || !command.CommandText.TrimStart().StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase) ||
                !command.CommandText.Contains(RuntimeHistoryProjectionSchema.HeadTable, StringComparison.Ordinal)) return result;
            Hits++; await using var advance = command.Connection!.CreateCommand(); advance.Transaction = command.Transaction;
            advance.CommandText = $"UPDATE {RuntimeHistoryProjectionSchema.HeadTable} SET Revision = Revision + 1";
            Assert.Equal(1, await advance.ExecuteNonQueryAsync(cancellationToken)); return result;
        }
    }
}
