using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Configuration;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;
using Xunit;

[assembly: EfSchemaFamily("occurrence-projection-controls", "1.0.0")]
[assembly: EfSchemaContent("occurrence-projection-controls", typeof(RuntimeHistoryOccurrenceEntity), "ContentJson")]
[assembly: EfSchemaContent("occurrence-projection-controls", typeof(RuntimeHistoryOccurrenceMembershipEntity), "ContentJson")]
[assembly: EfSchemaContent("occurrence-projection-controls", typeof(RuntimeHistoryOccurrenceCounterEntity), "ContentJson")]

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

// Actual isolated SQLite projection/transaction controls. Production registration, native journal
// commit, migrations, retention mutation, process restart and other providers are separate gates.
public sealed class RuntimeHistoryOccurrenceProjectionTests
{
    private static readonly EfSchemaChain Chain = EfSchemaChain.Of(typeof(RuntimeHistoryOccurrenceProjectionTests).Assembly, "occurrence-projection-controls");
    private static readonly DateTimeOffset At = DateTimeOffset.UnixEpoch;

    [Fact]
    public async Task Staging_requires_the_callers_transaction_and_never_saves_or_commits_it()
    {
        await using var db = await Database.Create(); await using var context = db.Open();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Stage(context, [Event(1, "a")]).AsTask());
        Assert.Empty(context.ChangeTracker.Entries());
        await using var transaction = await context.Database.BeginTransactionAsync();
        Assert.Equal(1, await Stage(context, [Event(1, "a")]));
        Assert.Same(transaction, context.Database.CurrentTransaction);
        Assert.Equal(0, await context.Set<RuntimeHistoryOccurrenceEntity>().CountAsync());
        await context.SaveChangesAsync();
        Assert.Equal(1, await context.Set<RuntimeHistoryOccurrenceEntity>().CountAsync());
        await transaction.RollbackAsync();
        await using var after = db.Open(); Assert.Equal(0, await after.Set<RuntimeHistoryOccurrenceCounterEntity>().CountAsync());
    }

    [Fact]
    public async Task First_completion_and_repeated_wait_resume_keep_one_exact_occurrence_and_dense_scope_ordinals()
    {
        await using var db = await Database.Create();
        await db.Commit([Event(1, "root"), Event(2, "loop-1", parent: "root"),
            Event(3, "loop-1", "ActivitySuspended", "root"), Event(4, "loop-1", "ActivityContinuationStarted", "root"),
            Event(5, "loop-2", "ActivityCompleted", "root")]);
        await using var read = db.Open();
        var rows = await read.Set<RuntimeHistoryOccurrenceEntity>().OrderBy(row => row.FirstSequence).ToArrayAsync();
        Assert.Equal(new long[] { 1, 2, 5 }, rows.Select(row => row.FirstSequence));
        Assert.Equal(new[] { "root", "loop-1", "loop-2" }, rows.Select(row => EfRelationalIdentity.Decode(row.ActivityExecutionId)));
        var rootScope = EfRuntimeHistoryOccurrenceProjection.CounterId("scope", "run", "root");
        var members = await read.Set<RuntimeHistoryOccurrenceMembershipEntity>().Where(row => row.ScopeOwnerId == rootScope).OrderBy(row => row.Ordinal).ToArrayAsync();
        Assert.Equal(new long[] { 0, 1, 2 }, members.Select(row => row.Ordinal));
        Assert.All(members, row => EfRuntimeHistoryOccurrenceProjection.ReadMembership(row, "scope", "run", Chain));
        var global = await Global(read);
        Assert.Equal(3, global.NextOrdinal); Assert.Equal(5, global.CoveredThroughSequence); Assert.Equal(1, global.ProjectionStartsAtSequence);
    }

    [Fact]
    public async Task Exact_replay_changes_no_receipt_or_counter_and_rollback_does_not_spend_an_ordinal()
    {
        await using var db = await Database.Create(); var first = Event(1, "a"); await db.Commit([first]);
        await using (var context = db.Open())
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            Assert.Equal(0, await Stage(context, [first])); Assert.Empty(context.ChangeTracker.Entries());
            Assert.Equal(0, await context.SaveChangesAsync()); await transaction.CommitAsync();
        }
        await using (var context = db.Open())
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            await Stage(context, [Event(2, "rolled-back")]); await context.SaveChangesAsync(); await transaction.RollbackAsync();
        }
        await db.Commit([Event(2, "b")]); await using var read = db.Open();
        var global = await Global(read); Assert.Equal(2, global.NextOrdinal);
        var owner = EfRuntimeHistoryOccurrenceProjection.CounterId("scope", "run", null);
        var selected = await read.Set<RuntimeHistoryOccurrenceMembershipEntity>().SingleAsync(row => row.ScopeOwnerId == owner && row.Ordinal == 1);
        Assert.Equal("b", EfRelationalIdentity.Decode(selected.ActivityExecutionId));
        Assert.Equal(2, await read.Set<RuntimeHistoryOccurrenceEntity>().CountAsync());
    }

    [Fact]
    public async Task Failed_validation_cannot_leave_a_partial_new_receipt_or_counter_in_the_tracker()
    {
        await using var db = await Database.Create(); await using var context = db.Open();
        await using var transaction = await context.Database.BeginTransactionAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => Stage(context, [Event(1, "a"), Event(2, "b", run: "other")]).AsTask());
        Assert.Empty(context.ChangeTracker.Entries()); Assert.Equal(0, await context.SaveChangesAsync());
        Assert.Equal(0, await context.Set<RuntimeHistoryOccurrenceEntity>().CountAsync());
        await transaction.CommitAsync();
    }

    [Fact]
    public async Task Corrupt_retained_content_is_checked_through_the_owner_chain_before_new_rows_are_staged()
    {
        await using var db = await Database.Create(); await db.Commit([Event(1, "a")]);
        await using (var corrupt = db.Open())
        { var row = await corrupt.Set<RuntimeHistoryOccurrenceCounterEntity>().SingleAsync(row => row.ActivityScopeId == null); row.ContentJson = "{}"; await corrupt.SaveChangesAsync(); }
        await using var context = db.Open(); await using var transaction = await context.Database.BeginTransactionAsync();
        await Assert.ThrowsAsync<RuntimeHistoryIntegrityException>(() => Stage(context, [Event(2, "b")]).AsTask());
        Assert.Empty(context.ChangeTracker.Entries()); Assert.Equal(0, await context.SaveChangesAsync());
        Assert.Equal(1, await context.Set<RuntimeHistoryOccurrenceEntity>().CountAsync());
    }

    [Fact]
    public async Task Later_ancestry_does_not_move_a_first_observation_to_another_scope_or_repair_unknown_membership()
    {
        await using var db = await Database.Create();
        await db.Commit([Event(1, "root"), Event(2, "left", parent: "root"), Event(3, "right", parent: "root"), Event(4, "unknown", known: false)]);
        await db.Commit([Event(5, "right", "ActivityContinuationStarted", "left"), Event(6, "unknown", "ActivityContinuationStarted", "left")]);
        await using var read = db.Open();
        var owner = EfRuntimeHistoryOccurrenceProjection.CounterId("scope", "run", "left");
        Assert.Equal(1, await read.Set<RuntimeHistoryOccurrenceMembershipEntity>().CountAsync(row => row.ScopeOwnerId == owner));
        var unknown = await read.Set<RuntimeHistoryOccurrenceEntity>().SingleAsync(row => row.ActivityExecutionIdHash == EfRelationalIdentity.Hash("unknown"));
        Assert.False(EfRuntimeHistoryOccurrenceProjection.ReadReceipt(unknown, "scope", "run", Chain).AncestryKnown);
        Assert.Equal(4, (await Global(read)).NextOrdinal);
    }

    [Fact]
    public async Task Unknown_vocabulary_and_missing_projection_baseline_remain_durable_limitations()
    {
        await using var db = await Database.Create();
        await db.Commit([Event(1, "a"), Event(2, "future", "FutureActivityKind")]);
        await db.Commit([Event(3, "future"), Event(4, "a", "ActivityContinuationStarted")]);
        await using var read = db.Open(); Assert.Equal(2, (await Global(read)).FirstUnknownObservationSequence);
        // A late provider projection cannot call later observations a complete from-start sequence.
        await db.Commit([Event(20, "legacy-continuation", "ActivityContinuationStarted", run: "late")], run: "late");
        var late = await read.Set<RuntimeHistoryOccurrenceCounterEntity>().SingleAsync(row => row.Id == EfRuntimeHistoryOccurrenceProjection.CounterId("scope", "late", null));
        Assert.Equal(20, EfRuntimeHistoryOccurrenceProjection.ReadCounter(late, "scope", "late", Chain).ProjectionStartsAtSequence);
    }

    [Fact]
    public async Task Retained_identity_receipts_prevent_a_continuation_from_becoming_another_occurrence()
    {
        await using var db = await Database.Create(); await db.Commit([Event(1, "a")]);
        // This isolated context stores no event content. The owner may retire that content, but
        // must retain these identity/membership rows until full run deletion.
        await db.Commit([Event(2, "a", "ActivityContinuationStarted")]);
        await using var read = db.Open(); Assert.Equal(1, await read.Set<RuntimeHistoryOccurrenceEntity>().CountAsync());
        Assert.Equal(1, (await Global(read)).NextOrdinal);
    }

    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public async Task Complete_encoded_scope_run_and_activity_identities_do_not_alias(int scenario)
    {
        var left = scenario switch { 0 => "Case", 1 => "a|b", _ => "a" + (char)0xD800 };
        var right = scenario switch { 0 => "case", 1 => "a", _ => "a\uFFFD" };
        await using var db = await Database.Create();
        await db.Commit([Event(1, left, run: left)], left, left);
        await db.Commit([Event(1, right, run: right)], right, right);
        await using var read = db.Open();
        var rows = await read.Set<RuntimeHistoryOccurrenceEntity>().ToArrayAsync(); Assert.Equal(2, rows.Length);
        foreach (var row in rows)
        {
            var rawScope = EfRelationalIdentity.Decode(row.ScopeKey); var rawRun = EfRelationalIdentity.Decode(row.WorkflowExecutionId);
            var data = EfRuntimeHistoryOccurrenceProjection.ReadReceipt(row, rawScope, rawRun, Chain);
            Assert.Equal(rawRun, EfRelationalIdentity.Decode(data.Activity));
        }
    }

    [Fact]
    public async Task A_stale_counter_cannot_overwrite_a_winning_transaction()
    {
        await using var db = await Database.Create(); await db.Commit([Event(1, "a")]);
        await using var stale = db.Open();
        _ = await stale.Set<RuntimeHistoryOccurrenceCounterEntity>().SingleAsync(row => row.ActivityScopeId == null);
        await db.Commit([Event(2, "b")]);
        await using (var transaction = await stale.Database.BeginTransactionAsync())
        {
            await Stage(stale, [Event(2, "a", "ActivityContinuationStarted")]);
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => stale.SaveChangesAsync());
            await transaction.RollbackAsync();
        }
        await using var read = db.Open(); Assert.Equal(2, (await Global(read)).NextOrdinal);
    }

    [Fact]
    public async Task Bounded_staging_and_dense_ordinal_lookup_do_not_walk_preceding_lifecycle_events()
    {
        await using var db = await Database.Create(); var facts = new List<RuntimeHistoryEvent>(); long sequence = 0;
        for (var index = 0; index < 200; index++)
        {
            facts.Add(Event(++sequence, "activity-" + index));
            for (var wait = 0; wait < 20; wait++) facts.Add(Event(++sequence, "activity-" + index, "ActivityContinuationStarted"));
        }
        await db.Commit(facts);
        var commands = new Commands(); await using var context = db.Open(commands);
        await using var transaction = await context.Database.BeginTransactionAsync();
        await Stage(context, [Event(sequence + 1, "new")]);
        Assert.InRange(commands.Reads, 1, 6);
        await context.SaveChangesAsync(); await transaction.CommitAsync();
        var owner = EfRuntimeHistoryOccurrenceProjection.CounterId("scope", "run", null);
        foreach (var ordinal in new long[] { 0, 100, 199, 200, 23 })
        {
            var row = await context.Set<RuntimeHistoryOccurrenceMembershipEntity>().AsNoTracking().SingleAsync(row => row.ScopeOwnerId == owner && row.Ordinal == ordinal);
            var data = EfRuntimeHistoryOccurrenceProjection.ReadMembership(row, "scope", "run", Chain);
            Assert.Equal(ordinal == 200 ? "new" : "activity-" + ordinal, EfRelationalIdentity.Decode(data.Activity));
        }
    }

    [Fact]
    public async Task Full_run_deletion_is_transactional_and_does_not_retire_another_physical_scope()
    {
        await using var db = await Database.Create(); await db.Commit([Event(1, "a")]); await db.Commit([Event(1, "a")], "other");
        await using (var context = db.Open())
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            await EfRuntimeHistoryOccurrenceProjection.StageDeleteAsync(context, "scope", "run"); await context.SaveChangesAsync();
            await transaction.RollbackAsync();
        }
        await using (var context = db.Open())
        {
            await using var transaction = await context.Database.BeginTransactionAsync();
            await EfRuntimeHistoryOccurrenceProjection.StageDeleteAsync(context, "scope", "run"); await context.SaveChangesAsync(); await transaction.CommitAsync();
        }
        await using var read = db.Open();
        Assert.Equal("other", EfRelationalIdentity.Decode(Assert.Single(await read.Set<RuntimeHistoryOccurrenceEntity>().ToArrayAsync()).ScopeKey));
        Assert.All(await read.Set<RuntimeHistoryOccurrenceMembershipEntity>().ToArrayAsync(), row => Assert.Equal(EfRelationalIdentity.Encode("other"), row.ScopeKey));
        Assert.All(await read.Set<RuntimeHistoryOccurrenceCounterEntity>().ToArrayAsync(), row => Assert.Equal(EfRelationalIdentity.Encode("other"), row.ScopeKey));
    }

    [Fact]
    public void Production_occurrence_model_preserves_frozen_membership_indexes_and_counter_concurrency()
    {
        using var context = new RuntimeSqliteDbContext(new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite("Data Source=:memory:").Options);
        var receipt = context.Model.FindEntityType(typeof(RuntimeHistoryOccurrenceEntity)); Assert.NotNull(receipt);
        var membership = context.Model.FindEntityType(typeof(RuntimeHistoryOccurrenceMembershipEntity)); Assert.NotNull(membership);
        var counter = context.Model.FindEntityType(typeof(RuntimeHistoryOccurrenceCounterEntity)); Assert.NotNull(counter);
        Assert.Contains(receipt.GetIndexes(), index => index.IsUnique && index.Properties.Select(property => property.Name)
            .SequenceEqual(["ScopeKeyHash", "WorkflowExecutionIdHash", "ActivityExecutionIdHash"]));
        Assert.Contains(membership.GetIndexes(), index => index.IsUnique && index.Properties.Select(property => property.Name).SequenceEqual(["ScopeOwnerId", "Ordinal"]));
        Assert.Contains(membership.GetIndexes(), index => index.IsUnique && index.Properties.Select(property => property.Name).SequenceEqual(["ScopeOwnerId", "FirstSequence"]));
        Assert.True(counter.FindProperty("Revision")!.IsConcurrencyToken);
    }

    private static RuntimeHistoryEvent Event(long sequence, string activity, string kind = "ActivityStarted", string? parent = null, bool known = true, string run = "run") =>
        new("event-" + sequence, run, sequence, RuntimeHistoryEventCategory.ActivityLifecycle, kind, At, activity)
        { Authority = RuntimeHistoryAuthority.CommittedTransition, RecordedAt = At, PhysicalCommitId = "physical", ParentActivityExecutionId = parent, ActivityAncestryKnown = known };
    private static ValueTask<int> Stage(DbContext context, IReadOnlyList<RuntimeHistoryEvent> events, string scope = "scope", string run = "run") =>
        EfRuntimeHistoryOccurrenceProjection.StageAsync(context, scope, run, Chain, events);
    private static async Task<EfRuntimeHistoryOccurrenceProjection.Counter> Global(DbContext context)
    {
        var row = await context.Set<RuntimeHistoryOccurrenceCounterEntity>().AsNoTracking().SingleAsync(row => row.Id == EfRuntimeHistoryOccurrenceProjection.CounterId("scope", "run", null));
        return EfRuntimeHistoryOccurrenceProjection.ReadCounter(row, "scope", "run", Chain);
    }
    private sealed class ProjectionContext(DbContextOptions<ProjectionContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        { RuntimeHistoryOccurrenceEntityConfiguration.Configure(modelBuilder); modelBuilder.IndexSchemaVersionStamps(); }
    }
    private sealed class Database(SqliteConnection connection) : IAsyncDisposable
    {
        internal static async Task<Database> Create()
        {
            var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
            var result = new Database(connection); await using var context = result.Open(); await context.Database.EnsureCreatedAsync(); return result;
        }
        internal ProjectionContext Open(Commands? commands = null)
        {
            var builder = new DbContextOptionsBuilder<ProjectionContext>().UseSqlite(connection);
            if (commands is not null) builder.AddInterceptors(commands);
            return new(builder.Options);
        }
        internal async Task Commit(IReadOnlyList<RuntimeHistoryEvent> events, string scope = "scope", string run = "run")
        {
            await using var context = Open(); await using var transaction = await context.Database.BeginTransactionAsync();
            await Stage(context, events, scope, run); await context.SaveChangesAsync(); await transaction.CommitAsync();
        }
        public ValueTask DisposeAsync() => connection.DisposeAsync();
    }
    private sealed class Commands : DbCommandInterceptor
    {
        public int Reads;
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken token = default)
        { if (command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)) Reads++; return ValueTask.FromResult(result); }
    }
}
