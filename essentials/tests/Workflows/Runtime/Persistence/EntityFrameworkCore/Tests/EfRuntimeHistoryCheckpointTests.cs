using System.Data.Common;
using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.Schema;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Configuration;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;
using Mapping = Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.EfRuntimeHistoryProjectionMapping;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>Private relational staging controls; the production writer/marker/provider is not enabled.</summary>
public sealed partial class EfRuntimeHistoryCheckpointTests
{
    private const string Scope = "tenant";
    private const string Run = "run";
    private static readonly DateTimeOffset Now = new(2030, 1, 2, 3, 4, 5, TimeSpan.Zero);
    private static readonly RuntimeHistoryCapturePolicy Policy = new("policy", 1, true,
        ["ActivityStarted", "ActivityCompleted", "ActivityResumeClaimed", "BookmarkCreated", "BookmarkConsumed", "BookmarkRemoved",
            "DeliveryReceived", "DeliveryDispatched", "DeliveryUnmatched"]);
    private static readonly EfSchemaChain Chain = EfSchemaChain.For(new EfSchemaFamilyDescriptor("RuntimeHistory", "Workflows.Runtime", "1.0.0", typeof(EfRuntimeHistoryCheckpointTests).Assembly)
    {
        ContentColumns = new[] { typeof(RuntimeHistoryHeadEntity), typeof(RuntimeHistoryEventEntity), typeof(RuntimeHistoryBookmarkEntity),
            typeof(RuntimeHistoryActivityEntity), typeof(RuntimeHistoryBookmarkCountEntity), typeof(RuntimeHistoryOccurrenceEntity),
            typeof(RuntimeHistoryOccurrenceMembershipEntity), typeof(RuntimeHistoryOccurrenceCounterEntity) }.Select(type => new EfSchemaColumn(type, "ContentJson")).ToArray()
    });

    [Fact]
    public async Task Ordered_logical_groups_allocate_once_and_later_physical_replay_keeps_original_event_evidence()
    {
        await using var database = await Database.CreateAsync();
        var batches = new[] { Batch("logical-1", Candidate("logical-1", "first", 0), Candidate("logical-1", "second", 1)),
            Batch("logical-2", Candidate("logical-2", "third", 0)) };
        var initial = await PersistAsync(database, Commit("physical-1", batches));
        Assert.Equal(new long[] { 1, 2, 3 }, initial.Select(item => item.Sequence));
        Assert.Equal(new[] { "first", "second", "third" }, initial.Select(item => item.HistoryEventId));
        Assert.All(initial, item => { Assert.Equal("physical-1", item.PhysicalCommitId); Assert.Equal(Now.AddDays(1), item.RecordedAt); Assert.False(item.OccurredAtKnown); });
        Assert.Empty(await PersistAsync(database, Commit("physical-2", batches)));
        await using var read = database.Open();
        var rows = await read.Set<RuntimeHistoryEventEntity>().OrderBy(item => item.Sequence).ToArrayAsync();
        Assert.Equal(3, rows.Length);
        Assert.All(rows, row => Assert.Equal("physical-1", RuntimeArtifactJson.Deserialize<RuntimeHistoryEvent>(row.ContentJson).PhysicalCommitId));
        Assert.Equal(3, (await read.Set<RuntimeHistoryHeadEntity>().SingleAsync()).LastSequence);
    }

    [Fact]
    public async Task History_cannot_stage_outside_a_caller_owned_native_transaction()
    {
        await using var database = await Database.CreateAsync(); await using var context = database.Open();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Stage(context, Commit("physical", [Batch("logical", Candidate("logical", "event", 0))])));
        Assert.Empty(await context.Set<RuntimeHistoryHeadEntity>().ToArrayAsync());
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task Conflicting_identity_in_one_or_later_group_cannot_allocate_another_position(bool sameGroup)
    {
        await using var database = await Database.CreateAsync();
        var first = Batch("logical", Candidate("logical", "same", 0));
        if (!sameGroup) await PersistAsync(database, Commit("first", [first]));
        await using var context = database.Open(); await using var transaction = await context.Database.BeginTransactionAsync();
        var conflict = sameGroup ? Commit("second", [first, Batch("other", Candidate("other", "same", 0))])
            : Commit("second", [Batch("logical", Candidate("logical", "same", 0, name: "ActivityCompleted"))]);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Stage(context, conflict));
        await transaction.RollbackAsync();
        await AssertRowsAsync(database, sameGroup ? 0 : 1);
    }

    [Fact]
    public async Task Required_contribution_absence_remains_owed_before_any_history_write()
    {
        await using var database = await Database.CreateAsync(); await using var context = database.Open();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var failure = await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() => Stage(context, Commit("physical", null)));
        Assert.Equal("runtime.history.capture-unavailable", failure.Code); await transaction.RollbackAsync();
        await AssertRowsAsync(database, 0);
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task An_unchanged_legacy_choice_keeps_plain_checkpoint_state_without_creating_history(bool legacyMarker)
    {
        await using var database = await Database.CreateAsync(explicitLegacy: true, legacyMarker: legacyMarker);
        string content; long revision;
        await using (var before = database.Open())
        {
            var row = await before.WorkflowExecutionStates.SingleAsync(); content = row.ContentJson; revision = row.Revision;
        }
        Assert.Empty(await PersistAsync(database, Commit("plain", null)));
        await using var after = database.Open(); var retained = await after.WorkflowExecutionStates.SingleAsync();
        Assert.Equal(content, retained.ContentJson); Assert.Equal(revision, retained.Revision);
        await AssertRowsAsync(database, 0);
    }

    [Fact]
    public async Task Missing_optional_contribution_pins_a_gap_that_later_capture_cannot_upgrade()
    {
        var optional = new RuntimeHistoryCapturePolicy(Policy.PolicyId, Policy.Version, false, Policy.SupportedKinds);
        await using var database = await Database.CreateAsync(optional);
        Assert.Empty(await PersistAsync(database, Commit("missing", null)));
        var candidate = Candidate("logical", "event", 0);
        await PersistAsync(database, Commit("later", [new(optional, "logical", Now, true, [candidate])]));
        await using var read = database.Open(); var row = await read.Set<RuntimeHistoryHeadEntity>().SingleAsync();
        var head = new Mapping(Chain).Read(row, Scope, Run);
        Assert.True(head.HasGaps); Assert.False(head.CapturedFromStart); Assert.Equal(1, head.LastSequence);
        Assert.True(optional.IsEquivalentTo(head.Policy));
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public async Task Incoming_state_cannot_clear_replace_or_retrofit_the_native_admission_choice(int change)
    {
        await using var database = await Database.CreateAsync(change == 2 ? null : Policy, explicitLegacy: change == 2);
        var incomingPolicy = change == 0 ? null : change == 1 ? new RuntimeHistoryCapturePolicy("replacement", 1, true, Policy.SupportedKinds) : Policy;
        var incoming = Workflow(incomingPolicy);
        var changes = new RuntimeCheckpointStateChangeSet(new(Run, RuntimeStateChangeOperation.Upsert, incoming, new Dictionary<string, string>()), null, [], [], [], [], []);
        await using var context = database.Open(); await using var transaction = await context.Database.BeginTransactionAsync();
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => Stage(context, Commit("physical", null, changes)));
        Assert.Contains("retain the native", failure.Message, StringComparison.Ordinal);
        await transaction.RollbackAsync(); await AssertRowsAsync(database, 0);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)] [InlineData(3)]
    public async Task Same_policy_cannot_retrofit_or_erase_the_nullable_admission_marker(int scenario)
    {
        var captured = scenario >= 2;
        var originalMarker = scenario is 0 or 3 ? (bool?)null : captured;
        var incomingMarker = scenario is 1 or 2 ? (bool?)null : captured;
        await using var database = await Database.CreateAsync(captured ? Policy : null, explicitLegacy: !captured, legacyMarker: originalMarker is null);
        var incoming = Workflow(captured ? Policy : null) with { HistoryCaptureEnabledAtAdmission = incomingMarker };
        var changes = new RuntimeCheckpointStateChangeSet(new(Run, RuntimeStateChangeOperation.Upsert, incoming, new Dictionary<string, string>()), null, [], [], [], [], []);
        await using var context = database.Open(); await using var transaction = await context.Database.BeginTransactionAsync();
        var commit = Commit("physical", captured ? [Batch("logical", Candidate("logical", "event", 0))] : null, changes);
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => Stage(context, commit));
        Assert.Contains("retain the native", failure.Message, StringComparison.Ordinal);
        await transaction.RollbackAsync(); await AssertRowsAsync(database, 0);
    }

    [Fact]
    public async Task Self_parent_ancestry_cannot_commit_a_projection_that_its_reader_refuses()
    {
        await using var database = await Database.CreateAsync(); await using var context = database.Open();
        await using var transaction = await context.Database.BeginTransactionAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Stage(context,
            Commit("physical", [Batch("logical", Candidate("logical", "event", 0, parent: "activity"))])));
        await transaction.RollbackAsync(); await AssertRowsAsync(database, 0);
    }

    [Fact]
    public async Task Caller_rollback_removes_reserved_head_and_events_after_a_native_sibling_write_fails()
    {
        await using var database = await Database.CreateAsync();
        await using (var context = database.Open())
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            await Stage(context, Commit("physical", [Batch("logical", Candidate("logical", "event", 0))]));
            // A real native PK violation after history's first reservation save; marker integration is a later unit.
            context.WorkflowExecutionStates.Add(EfWorkflowExecutionStateStore.ToEntity(Workflow(Policy), Scope, EfWorkflowExecutionStateStore.CreateId(Scope, Run), 1));
            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
            await transaction.RollbackAsync();
        }
        await AssertRowsAsync(database, 0);
    }

    [Fact]
    public async Task Explicit_head_reservation_cannot_overwrite_a_newer_revision()
    {
        await using var database = await Database.CreateAsync();
        await PersistAsync(database, Commit("first", [Batch("logical", Candidate("logical", "event", 0))]));
        await using var stale = database.Open(); var old = await stale.Set<RuntimeHistoryHeadEntity>().SingleAsync();
        await using (var winner = database.Open())
            await winner.Set<RuntimeHistoryHeadEntity>().ExecuteUpdateAsync(set => set.SetProperty(row => row.Revision, row => row.Revision + 1));
        await using var transaction = await stale.Database.BeginTransactionAsync();
        var mapping = new Mapping(Chain); var next = mapping.Read(old, Scope, Run) with { LastSequence = 2 };
        await Assert.ThrowsAsync<EfRuntimeHistoryCheckpoint.HeadAllocationConflictException>(() =>
            EfRuntimeHistoryCheckpoint.ReserveHeadAsync(stale, mapping, Chain, Scope, Run, old, next, CancellationToken.None).AsTask());
        await transaction.RollbackAsync(); await AssertRowsAsync(database, 1);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public async Task Exact_receipts_distinguish_acknowledged_purge_from_unexplained_missing_content(int mode)
    {
        await using var database = await Database.CreateAsync(); var batch = Batch("logical", Candidate("logical", "event", 0));
        await PersistAsync(database, Commit("first", [batch]));
        await using (var retire = database.Open())
        {
            var row = await retire.Set<RuntimeHistoryEventEntity>().SingleAsync(); row.ContentJson = ""; row.IsPurged = mode != 2; row.Revision++;
            if (mode == 0)
            {
                var headRow = await retire.Set<RuntimeHistoryHeadEntity>().SingleAsync(); var head = new Mapping(Chain).Read(headRow, Scope, Run);
                headRow.FirstSequence = 2; headRow.PurgedThroughSequence = 1; headRow.Revision++;
                headRow.ContentJson = RuntimeArtifactJson.Serialize(head with { FirstSequence = 2, PurgedThroughSequence = 1 });
            }
            await retire.SaveChangesAsync();
        }
        await using var context = database.Open(); await using var transaction = await context.Database.BeginTransactionAsync();
        if (mode == 0) { Assert.Empty(await Stage(context, Commit("later", [batch]))); await context.SaveChangesAsync(); await transaction.CommitAsync(); }
        else { await Assert.ThrowsAsync<RuntimeHistoryIntegrityException>(() => Stage(context, Commit("later", [batch]))); await transaction.RollbackAsync(); }
        await using var read = database.Open(); Assert.Single(await read.Set<RuntimeHistoryEventEntity>().ToArrayAsync());
        Assert.Empty((await read.Set<RuntimeHistoryEventEntity>().SingleAsync()).ContentJson);
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task Bookmark_create_consume_counts_and_unknown_ending_time_survive_without_reactivation(bool knownTime)
    {
        await using var database = await Database.CreateAsync(); var summary = Bookmark();
        await PersistAsync(database, Commit("first", [Batch("logical",
            Candidate("logical", "created", 0, new(summary, RuntimeHistoryBookmarkStatus.Active)),
            Candidate("logical", "consumed", 1, new(summary, RuntimeHistoryBookmarkStatus.Consumed), knownTime: knownTime))]));
        await using (var read = database.Open())
        {
            var head = new Mapping(Chain).Read(await read.Set<RuntimeHistoryHeadEntity>().SingleAsync(), Scope, Run);
            var item = new Mapping(Chain).Read(await read.Set<RuntimeHistoryBookmarkEntity>().SingleAsync(), Scope, Run, head);
            Assert.Equal(RuntimeHistoryBookmarkStatus.Consumed, item.Status); Assert.Equal("created", item.CreatedHistoryEventId);
            Assert.Equal(knownTime ? Now : (DateTimeOffset?)null, item.EndedAt);
            var counts = await read.Set<RuntimeHistoryBookmarkCountEntity>().ToArrayAsync(); Assert.Equal(2, counts.Length);
            Assert.All(counts, count => { Assert.Equal(1, count.TotalCount); Assert.Equal(0, count.ActiveCount); });
        }
        await using var context = database.Open(); await using var transaction = await context.Database.BeginTransactionAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Stage(context, Commit("rearm", [Batch("rearm", Candidate("rearm", "new-create", 0, new(summary, RuntimeHistoryBookmarkStatus.Active)))])));
        await transaction.RollbackAsync(); await AssertRowsAsync(database, 2);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public async Task Immutable_bookmark_definition_includes_exact_original_offsets_and_expiry(int change)
    {
        await using var database = await Database.CreateAsync(); var original = Bookmark();
        await PersistAsync(database, Commit("first", [Batch("create", Candidate("create", "created", 0, new(original, RuntimeHistoryBookmarkStatus.Active)))]));
        var changed = change switch { 0 => original with { StimulusType = "other" }, 1 => original with { CreatedAt = original.CreatedAt.ToOffset(TimeSpan.FromHours(1)) },
            _ => original with { ExpiresAt = Now.AddHours(2) } };
        await using var context = database.Open(); await using var transaction = await context.Database.BeginTransactionAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Stage(context, Commit("consume", [Batch("consume", Candidate("consume", "consumed", 0, new(changed, RuntimeHistoryBookmarkStatus.Consumed)))])));
        await transaction.RollbackAsync(); await AssertRowsAsync(database, 1);
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task Cleanup_needs_a_current_physical_or_same_group_creation_source(bool createdInGroup)
    {
        await using var database = await Database.CreateAsync(); var summary = Bookmark();
        var cleanup = new ActivityScopeCleanupRequest(Run, "scope", ["activity"], ["bookmark"], [], []) { HistoryBookmarks = [summary] };
        var changes = new RuntimeCheckpointStateChangeSet(null, null, [], [], [], [], [], activityScopeCleanups: [cleanup]);
        var candidates = new List<RuntimeHistoryCandidate>();
        if (createdInGroup) candidates.Add(Candidate("cleanup", "created", 0, new(summary, RuntimeHistoryBookmarkStatus.Active)));
        candidates.Add(Candidate("cleanup", "removed", candidates.Count, new(summary, RuntimeHistoryBookmarkStatus.Removed)));
        var commit = Commit("physical", [Batch("cleanup", candidates.ToArray())], changes);
        if (createdInGroup) { Assert.Equal(2, (await PersistAsync(database, commit)).Count); return; }
        await using var context = database.Open(); await using var transaction = await context.Database.BeginTransactionAsync();
        var failure = await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() => Stage(context, commit));
        Assert.Equal("runtime.history.cleanup-source-unavailable", failure.Code); await transaction.RollbackAsync(); await AssertRowsAsync(database, 0);
    }

    [Fact]
    public async Task Scope_retirement_during_the_actual_head_read_refuses_publication()
    {
        await using var database = await Database.CreateAsync(); var access = new Accessor();
        await using var context = database.Open(new RetireAccess(access)); await using var transaction = await context.Database.BeginTransactionAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Stage(context, Commit("physical", [Batch("logical", Candidate("logical", "event", 0))]), access));
        await transaction.RollbackAsync(); await AssertRowsAsync(database, 0);
    }

    [Fact]
    public async Task Checkpoint_staging_projects_first_completion_and_child_membership_once_across_resume()
    {
        await using var database = await Database.CreateAsync();
        await PersistAsync(database, Commit("first", [Batch("logical",
            Candidate("logical", "root-completed", 0, name: "ActivityCompleted", activity: "root"),
            Candidate("logical", "child-started", 1, parent: "root", activity: "child"),
            Candidate("logical", "root-later", 2, activity: "root"),
            Candidate("logical", "child-later", 3, name: "ActivityCompleted", parent: "root", activity: "child"))]));
        await PersistAsync(database, Commit("resume", [Batch("resume", Candidate("resume", "root-resumed", 0, name: "ActivityResumeClaimed", activity: "root"))]));
        await using var read = database.Open();
        var receipts = await read.Set<RuntimeHistoryOccurrenceEntity>().OrderBy(row => row.FirstSequence).ToArrayAsync();
        Assert.Equal(new[] { "root-completed", "child-started" }, receipts.Select(row => EfRelationalIdentity.Decode(row.FirstHistoryEventId)));
        Assert.Equal(new long[] { 1, 2 }, receipts.Select(row => row.FirstSequence));
        Assert.All(receipts, row => Assert.True(row.AncestryKnown));
        var rootScope = EfRelationalIdentity.Encode("root");
        var descendants = await read.Set<RuntimeHistoryOccurrenceMembershipEntity>().Where(row => row.ActivityScopeId == rootScope)
            .OrderBy(row => row.Ordinal).ToArrayAsync();
        Assert.Equal(new[] { "root", "child" }, descendants.Select(row => EfRelationalIdentity.Decode(row.ActivityExecutionId)));
        Assert.Equal(new long[] { 0, 1 }, descendants.Select(row => row.Ordinal));
        var global = await read.Set<RuntimeHistoryOccurrenceCounterEntity>().SingleAsync(row => row.ActivityScopeId == null);
        Assert.Equal(2, global.NextOrdinal); Assert.Equal(5, global.CoveredThroughSequence);
        Assert.Equal(5, (await read.Set<RuntimeHistoryHeadEntity>().SingleAsync()).LastSequence);
    }

    [Fact]
    public async Task Occurrence_insert_failure_rolls_back_the_earlier_head_reservation_and_journal()
    {
        await using var database = await Database.CreateAsync();
        await using (var context = database.Open())
        {
            await context.Database.ExecuteSqlRawAsync("""
                CREATE TRIGGER fail_occurrence_member BEFORE INSERT ON elsa_runtime_history_occurrence_member
                BEGIN SELECT RAISE(ABORT, 'controlled occurrence insert failure'); END;
                """);
            await using var transaction = await context.Database.BeginTransactionAsync();
            await Stage(context, Commit("physical", [Batch("logical", Candidate("logical", "event", 0))]));
            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
            await transaction.RollbackAsync();
        }
        await AssertRowsAsync(database, 0);
    }

    [Fact]
    public async Task A_chain_omitting_occurrence_content_cannot_commit_a_partial_projection()
    {
        await using var database = await Database.CreateAsync(); await using var context = database.Open();
        await using var transaction = await context.Database.BeginTransactionAsync();
        var incomplete = EfSchemaChain.For(new EfSchemaFamilyDescriptor("RuntimeHistory", "Workflows.Runtime", "1.0.0", typeof(EfRuntimeHistoryCheckpointTests).Assembly)
        { ContentColumns = Chain.ContentColumns.Where(column => !column.Entity.Name.StartsWith("RuntimeHistoryOccurrence", StringComparison.Ordinal)).ToArray() });
        await Assert.ThrowsAsync<InvalidOperationException>(() => EfRuntimeHistoryCheckpoint.StageAsync(context, new Accessor(),
            Commit("physical", [Batch("logical", Candidate("logical", "event", 0))]), Scope, Now.AddDays(1), incomplete, CancellationToken.None).AsTask());
        await transaction.RollbackAsync(); await AssertRowsAsync(database, 0);
    }

    private static RuntimeHistoryCandidate Candidate(string checkpoint, string id, int ordinal, RuntimeHistoryBookmarkMutation? mutation = null,
        string name = "ActivityStarted", bool knownTime = false, string? parent = null, string activity = "activity")
    {
        if (mutation is not null) name = mutation.Status switch { RuntimeHistoryBookmarkStatus.Active => "BookmarkCreated",
            RuntimeHistoryBookmarkStatus.Consumed => "BookmarkConsumed", _ => "BookmarkRemoved" };
        return new(new(id, Run, 0, mutation is null ? RuntimeHistoryEventCategory.ActivityLifecycle : RuntimeHistoryEventCategory.BookmarkLifecycle,
            name, Now, activity, checkpointId: checkpoint) { Authority = RuntimeHistoryAuthority.CommittedTransition,
            BookmarkId = mutation?.Bookmark.Identity.BookmarkId, OccurredAtKnown = knownTime, ActivityAncestryKnown = true,
            ParentActivityExecutionId = parent }, ordinal, mutation);
    }
    private static RuntimeHistoryBatch Batch(string checkpoint, params RuntimeHistoryCandidate[] candidates) => new(Policy, checkpoint, Now, true, candidates);
    private static RuntimeCheckpointCommit Commit(string id, IReadOnlyList<RuntimeHistoryBatch>? batches, RuntimeCheckpointStateChangeSet? changes = null) =>
        new(id, new(id, "control", Run, Now, [], new Dictionary<string, string>()), changes ?? new(null, null, [], [], [], [], []), [], new Dictionary<string, string>()) { HistoryBatches = batches };
    private static ActivityExecutionBookmarkSummary Bookmark() => new(new("bookmark", Run, "activity", "resume"), "signal", "hash", Now, Now.AddHours(1));
    private static WorkflowExecutionState Workflow(RuntimeHistoryCapturePolicy? policy, WorkflowExecutionStatus status = WorkflowExecutionStatus.Running) => new(Run,
        new WorkflowExecutableIdentity("artifact", "definition", "version", "1", "hash"), status,
        null, Now, Now, Now, status.IsTerminal() ? Now : null, null, null, Scope, new Dictionary<string, string>()) { HistoryCapturePolicy = policy, HistoryCaptureEnabledAtAdmission = policy is not null };
    private static Task<IReadOnlyList<RuntimeHistoryEvent>> Stage(RuntimeDbContext context, RuntimeCheckpointCommit commit, Accessor? access = null) =>
        EfRuntimeHistoryCheckpoint.StageAsync(context, access ?? new(), commit, Scope, Now.AddDays(1), Chain, CancellationToken.None).AsTask();
    private static async Task<IReadOnlyList<RuntimeHistoryEvent>> PersistAsync(Database database, RuntimeCheckpointCommit commit)
    {
        await using var context = database.Open(); await using var transaction = await context.Database.BeginTransactionAsync();
        var events = await Stage(context, commit); await context.SaveChangesAsync(); await transaction.CommitAsync(); return events;
    }
    private static async Task AssertRowsAsync(Database database, int count)
    {
        await using var context = database.Open(); Assert.Equal(count, await context.Set<RuntimeHistoryEventEntity>().CountAsync());
        if (count == 0)
        {
            Assert.Empty(await context.Set<RuntimeHistoryHeadEntity>().ToArrayAsync()); Assert.Empty(await context.Set<RuntimeHistoryBookmarkEntity>().ToArrayAsync());
            Assert.Empty(await context.Set<RuntimeHistoryActivityEntity>().ToArrayAsync()); Assert.Empty(await context.Set<RuntimeHistoryBookmarkCountEntity>().ToArrayAsync());
            Assert.Empty(await context.Set<RuntimeHistoryOccurrenceEntity>().ToArrayAsync());
            Assert.Empty(await context.Set<RuntimeHistoryOccurrenceMembershipEntity>().ToArrayAsync());
            Assert.Empty(await context.Set<RuntimeHistoryOccurrenceCounterEntity>().ToArrayAsync());
        }
        else Assert.Equal(count, (await context.Set<RuntimeHistoryHeadEntity>().SingleAsync()).LastSequence);
    }
    private sealed class Accessor : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; set; } = PersistenceAccessContext.Scoped(new(Scope));
    }
    private sealed class RetireAccess(Accessor access) : DbCommandInterceptor
    {
        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData data, DbDataReader result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains(RuntimeHistoryProjectionSchema.HeadTable, StringComparison.Ordinal))
                access.Current = PersistenceAccessContext.Scoped(new("other"));
            return ValueTask.FromResult(result);
        }
    }
    private sealed class HistoryContext(DbContextOptions<HistoryContext> options) : RuntimeDbContext(options)
    {
        protected override void ConfigureProvider(ModelBuilder builder)
        {
            RuntimeHistoryEntityConfiguration.Configure(builder);
            RuntimeHistoryOccurrenceEntityConfiguration.Configure(builder);
        }
    }
    private sealed class Database(SqliteConnection keeper) : IAsyncDisposable
    {
        internal static async Task<Database> CreateAsync(RuntimeHistoryCapturePolicy? policy = null, bool explicitLegacy = false, bool legacyMarker = false,
            WorkflowExecutionStatus status = WorkflowExecutionStatus.Running)
        {
            var keeper = new SqliteConnection("Data Source=:memory:"); await keeper.OpenAsync(); var database = new Database(keeper);
            await using var context = database.Open(); await context.Database.EnsureCreatedAsync(); var state = Workflow(explicitLegacy ? null : policy ?? Policy, status);
            if (legacyMarker) state = state with { HistoryCaptureEnabledAtAdmission = null };
            context.WorkflowExecutionStates.Add(EfWorkflowExecutionStateStore.ToEntity(state, Scope, EfWorkflowExecutionStateStore.CreateId(Scope, Run), 1));
            await context.SaveChangesAsync(); return database;
        }
        internal HistoryContext Open(IInterceptor? interceptor = null)
        {
            var options = new DbContextOptionsBuilder<HistoryContext>().UseSqlite(keeper);
            if (interceptor is not null) options.AddInterceptors(interceptor);
            return new(options.Options);
        }
        public ValueTask DisposeAsync() => keeper.DisposeAsync();
    }
}
