using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class EfRuntimeHistoryCheckpointTests
{
    [Theory]
    [InlineData(WorkflowExecutionStatus.Running)]
    [InlineData(WorkflowExecutionStatus.Completed)]
    public async Task Independent_observation_preserves_native_coverage_and_occurrence_count(WorkflowExecutionStatus status)
    {
        await using var database = await Database.CreateAsync(status: status);
        await PersistAsync(database, Commit("native-checkpoint", [Batch("native", Candidate("native", "native-event", 0))]));
        string nativeContent; long nativeRevision;
        await using (var before = database.Open())
        {
            var row = await before.WorkflowExecutionStates.SingleAsync(); nativeContent = row.ContentJson; nativeRevision = row.Revision;
        }
        var result = await PersistObservationAsync(database, Observation("delivery"));
        Assert.True(result.Recorded); Assert.NotNull(result.Event); Assert.Equal(2, result.Event.Sequence);
        Assert.Equal(RuntimeHistoryAuthority.DeliveryObservation, result.Event.Authority); Assert.False(result.Event.OccurredAtKnown);
        Assert.Equal("owner-observation:delivery", result.Event.PhysicalCommitId);
        await using var read = database.Open(); var headRow = await read.Set<RuntimeHistoryHeadEntity>().SingleAsync();
        var head = new EfRuntimeHistoryProjectionMapping(Chain).Read(headRow, Scope, Run);
        Assert.Equal("native-checkpoint", head.LastPhysicalCommitId); Assert.Equal(Now.AddDays(1), head.LastCommittedAt);
        Assert.True(head.CapturedFromStart); Assert.False(head.HasGaps);
        var global = await read.Set<RuntimeHistoryOccurrenceCounterEntity>().SingleAsync(row => row.ActivityScopeId == null);
        Assert.Equal(1, global.NextOrdinal); Assert.Equal(2, global.CoveredThroughSequence);
        Assert.Single(await read.Set<RuntimeHistoryOccurrenceEntity>().ToArrayAsync());
        Assert.Empty(await read.Set<RuntimeHistoryBookmarkEntity>().ToArrayAsync()); Assert.Empty(await read.Bookmarks.ToArrayAsync());
        var native = await read.WorkflowExecutionStates.SingleAsync(); Assert.Equal(nativeContent, native.ContentJson); Assert.Equal(nativeRevision, native.Revision);
        Assert.All(await read.Set<RuntimeHistoryEventEntity>().ToArrayAsync(), row => Assert.Equal(64, row.CandidateFingerprint.Length));
    }

    [Fact]
    public async Task Observation_replay_returns_original_recording_without_spending_another_position()
    {
        await using var database = await CapturedDatabaseAsync(); var original = Observation("delivery");
        var first = await PersistObservationAsync(database, original);
        var replay = await PersistObservationAsync(database, original, Now.AddDays(10));
        Assert.True(replay.Recorded); Assert.Equal(first.Event!.HistoryEventId, replay.Event!.HistoryEventId);
        Assert.Equal(first.Event.Sequence, replay.Event.Sequence); Assert.Equal(first.Event.RecordedAt, replay.Event.RecordedAt);
        await AssertRowsAsync(database, 2);
    }

    [Fact]
    public async Task Observation_advances_the_prefix_before_the_next_native_checkpoint_occurrence()
    {
        await using var database = await CapturedDatabaseAsync(); await PersistObservationAsync(database, Observation("delivery"));
        await PersistAsync(database, Commit("next-native", [Batch("next", Candidate("next", "completed-next", 0,
            name: "ActivityCompleted", parent: "activity", activity: "next"))]));
        await using var read = database.Open(); var global = await read.Set<RuntimeHistoryOccurrenceCounterEntity>().SingleAsync(row => row.ActivityScopeId == null);
        Assert.Equal(3, global.CoveredThroughSequence); Assert.Equal(2, global.NextOrdinal);
        Assert.Equal(new long[] { 1, 3 }, await read.Set<RuntimeHistoryOccurrenceEntity>().OrderBy(row => row.FirstSequence).Select(row => row.FirstSequence).ToArrayAsync());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Association_without_a_captured_head_cannot_create_coverage(bool legacy)
    {
        await using var database = await Database.CreateAsync(explicitLegacy: legacy);
        var result = await PersistObservationAsync(database, Observation("delivery"));
        Assert.False(result.Recorded); Assert.Null(result.Event); await AssertRowsAsync(database, 0);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Conflicting_observation_or_native_candidate_identity_cannot_be_overwritten(bool nativeCollision)
    {
        await using var database = await CapturedDatabaseAsync(nativeCollision ? "shared" : "native-event");
        if (!nativeCollision) await PersistObservationAsync(database, Observation("shared"));
        await using var context = database.Open(); await using var transaction = await context.Database.BeginTransactionAsync();
        await Assert.ThrowsAsync<RuntimeHistoryIntegrityException>(() => StageObservation(context, Observation("shared", Now.AddMinutes(1))));
        await transaction.RollbackAsync(); await AssertRowsAsync(database, nativeCollision ? 1 : 2);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Observation_receipt_replay_requires_acknowledged_content_purge(bool acknowledged)
    {
        await using var database = await CapturedDatabaseAsync(); await PersistObservationAsync(database, Observation("delivery"));
        await using (var retire = database.Open())
        {
            if (acknowledged)
            {
                var row = await retire.Set<RuntimeHistoryHeadEntity>().SingleAsync(); var head = new EfRuntimeHistoryProjectionMapping(Chain).Read(row, Scope, Run);
                row.FirstSequence = 3; row.PurgedThroughSequence = 2; row.Revision++;
                row.ContentJson = RuntimeArtifactJson.Serialize(head with { FirstSequence = 3, PurgedThroughSequence = 2 });
                await retire.Set<RuntimeHistoryEventEntity>().ExecuteUpdateAsync(set => set.SetProperty(item => item.IsPurged, true).SetProperty(item => item.ContentJson, ""));
            }
            else
            {
                var row = await retire.Set<RuntimeHistoryEventEntity>().SingleAsync(item => item.Sequence == 2); row.ContentJson = ""; row.IsPurged = true;
            }
            await retire.SaveChangesAsync();
        }
        await using var context = database.Open(); await using var transaction = await context.Database.BeginTransactionAsync();
        if (acknowledged)
        {
            var replay = await StageObservation(context, Observation("delivery")); Assert.True(replay.Recorded); Assert.Null(replay.Event);
            await context.SaveChangesAsync(); await transaction.CommitAsync();
        }
        else { await Assert.ThrowsAsync<RuntimeHistoryIntegrityException>(() => StageObservation(context, Observation("delivery"))); await transaction.RollbackAsync(); }
        await using var read = database.Open(); Assert.Equal(2, (await read.Set<RuntimeHistoryHeadEntity>().SingleAsync()).LastSequence);
        Assert.Empty((await read.Set<RuntimeHistoryEventEntity>().SingleAsync(row => row.Sequence == 2)).ContentJson);
    }

    [Fact]
    public async Task Derived_prefix_save_failure_rolls_back_the_independent_head_and_event()
    {
        await using var database = await CapturedDatabaseAsync();
        await using (var context = database.Open())
        {
            await context.Database.ExecuteSqlRawAsync("""
                CREATE TRIGGER fail_observation_counter BEFORE UPDATE ON elsa_runtime_history_occurrence_counter
                BEGIN SELECT RAISE(ABORT, 'controlled observation counter failure'); END;
                """);
            await using var transaction = await context.Database.BeginTransactionAsync();
            var staged = await StageObservation(context, Observation("delivery")); Assert.True(staged.Recorded);
            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync()); await transaction.RollbackAsync();
        }
        await AssertRowsAsync(database, 1); await using var read = database.Open();
        Assert.Equal(1, (await read.Set<RuntimeHistoryOccurrenceCounterEntity>().SingleAsync(row => row.ActivityScopeId == null)).CoveredThroughSequence);
    }

    [Fact]
    public async Task Observation_policy_must_still_equal_the_retained_native_policy()
    {
        await using var database = await CapturedDatabaseAsync(); await using var context = database.Open();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var requested = Observation("delivery") with { Policy = new("changed", 1, true, Policy.SupportedKinds) };
        await Assert.ThrowsAsync<RuntimeHistoryIntegrityException>(() => StageObservation(context, requested));
        await transaction.RollbackAsync(); await AssertRowsAsync(database, 1);
    }

    [Fact]
    public async Task Scope_retirement_during_observation_admission_cannot_publish_the_result()
    {
        await using var database = await CapturedDatabaseAsync(); var access = new Accessor();
        await using var context = database.Open(new RetireAccess(access)); await using var transaction = await context.Database.BeginTransactionAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => StageObservation(context, Observation("delivery"), access: access));
        await transaction.RollbackAsync(); await AssertRowsAsync(database, 1);
    }

    private static RuntimeHistoryObservation Observation(string id, DateTimeOffset? occurredAt = null) => new(Policy,
        new(id, Run, 0, RuntimeHistoryEventCategory.DeliveryObservation, "DeliveryReceived", occurredAt ?? Now, "activity")
        { Authority = RuntimeHistoryAuthority.DeliveryObservation, OccurredAtKnown = false, BookmarkId = "bookmark", Causation = new(DeliveryId: "delivery") });
    private static Task<RuntimeHistoryObservationResult> StageObservation(HistoryContext context, RuntimeHistoryObservation observation,
        DateTimeOffset? recordedAt = null, Accessor? access = null) => EfRuntimeHistoryObservation.StageAsync(context, access ?? new(), observation,
            Scope, recordedAt ?? Now.AddDays(2), Chain, CancellationToken.None).AsTask();
    private static async Task<RuntimeHistoryObservationResult> PersistObservationAsync(Database database, RuntimeHistoryObservation observation, DateTimeOffset? recordedAt = null)
    {
        await using var context = database.Open(); await using var transaction = await context.Database.BeginTransactionAsync();
        var staged = await StageObservation(context, observation, recordedAt); await context.SaveChangesAsync(); await transaction.CommitAsync(); return staged;
    }
    private static async Task<Database> CapturedDatabaseAsync(string eventId = "native-event")
    {
        var database = await Database.CreateAsync();
        await PersistAsync(database, Commit("native-checkpoint", [Batch("native", Candidate("native", eventId, 0))]));
        return database;
    }
}
