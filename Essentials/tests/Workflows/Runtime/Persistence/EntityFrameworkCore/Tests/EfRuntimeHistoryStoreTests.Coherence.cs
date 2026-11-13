using System.Data.Common;
using System.Text.Json.Nodes;
using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.Tests;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;
using Mapping = Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.EfRuntimeHistoryProjectionMapping;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class EfRuntimeHistoryStoreTests
{
    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public async Task Mutable_bookmark_details_and_counts_share_the_head_snapshot_during_a_real_concurrent_commit(bool inventory, bool caller)
    {
        await using var seed = await Database.Create(); await seed.Append(new Fact("created", "a", "BookmarkCreated", Mutation: Mutation("b", "a")));
        await using var file = new TemporarySqliteDatabase("selected-history-snapshot");
        await using (var target = new SqliteConnection(file.ConnectionString))
        { await target.OpenAsync(); seed.BackupTo(target); await using var command = target.CreateCommand(); command.CommandText = "PRAGMA journal_mode=WAL"; await command.ExecuteNonQueryAsync(); }
        await using var connection = new SqliteConnection(file.ConnectionString); await connection.OpenAsync();
        await using var nativeTransaction = caller ? connection.BeginTransaction(System.Data.IsolationLevel.Serializable, deferred: true) : null;
        var gate = new SnapshotGate();
        await using var context = new HistoryContext(new DbContextOptionsBuilder<HistoryContext>().UseSqlite(connection).AddInterceptors(gate).Options);
        await using var transaction = caller ? await context.Database.UseTransactionAsync(nativeTransaction) : null;
        var store = seed.Store(context);
        var pending = inventory ? ReadInventory() : ReadDetails();
        await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
        await using (var writer = new HistoryContext(new DbContextOptionsBuilder<HistoryContext>().UseSqlite(file.ConnectionString).Options))
        {
            await using var writeTransaction = await writer.Database.BeginTransactionAsync();
            var logical = "concurrent-consume"; var mutation = Mutation("b", "a", RuntimeHistoryBookmarkStatus.Consumed);
            var item = new RuntimeHistoryEvent("consumed", "run", 0, RuntimeHistoryEventCategory.BookmarkLifecycle, "BookmarkConsumed", At.AddMinutes(1), "a", checkpointId: logical)
            { Authority = RuntimeHistoryAuthority.CommittedTransition, BookmarkId = "b", ActivityAncestryKnown = true };
            var batch = new RuntimeHistoryBatch(Policy, logical, At, false, [new(item, 0, mutation)]);
            var commit = new RuntimeCheckpointCommit("concurrent-physical", new(logical, "control", "run", At, [], new Dictionary<string, string>()),
                new(null, null, [], [], [], [], []), [], new Dictionary<string, string>()) { HistoryBatches = [batch] };
            await EfRuntimeHistoryCheckpoint.StageAsync(writer, new Access(), commit, "scope", At.AddMinutes(2), Chain, CancellationToken.None);
            await writer.SaveChangesAsync(); await writeTransaction.CommitAsync();
        }
        gate.Release.SetResult(); await pending.WaitAsync(TimeSpan.FromSeconds(20));
        if (caller) { Assert.Same(transaction, context.Database.CurrentTransaction); await transaction!.RollbackAsync(); }
        else Assert.Null(context.Database.CurrentTransaction);
        await using var after = new HistoryContext(new DbContextOptionsBuilder<HistoryContext>().UseSqlite(file.ConnectionString).Options);
        Assert.Equal(RuntimeHistoryBookmarkStatus.Consumed, (await seed.Store(after).FindBookmarkAsync("run", "b"))!.Status);
        async Task ReadInventory()
        { var page = await store.ListBookmarksAsync(new("run")); Assert.Equal(1, page.Head!.LastSequence); Assert.Equal(1, page.ActiveCount);
          Assert.Equal(RuntimeHistoryBookmarkStatus.Active, Assert.Single(page.Items).Status); }
        async Task ReadDetails()
        { var page = await store.FindBookmarkDetailsAsync("run", "b"); Assert.Equal(1, page.Head!.LastSequence); Assert.Equal(RuntimeHistoryBookmarkStatus.Active, page.Bookmark!.Status); }
    }

    [Fact]
    public async Task Known_parent_cycles_fail_as_integrity_instead_of_hanging_or_silently_excluding_rows()
    {
        await using var db = await Database.Create(); await db.Append(new Fact("root", "root"), new("a", "a", Parent: "root"), new("b", "b", Parent: "a"));
        await using (var corrupt = db.Open())
        {
            var row = await corrupt.Set<RuntimeHistoryActivityEntity>().SingleAsync(item => item.ActivityExecutionId == EfRelationalIdentity.Encode("a"));
            var next = new Mapping(Chain).Row("scope", "run", new Mapping.ActivityParent("a", "b")); next.Revision = row.Revision + 1;
            corrupt.Entry(row).CurrentValues.SetValues(next); await corrupt.SaveChangesAsync();
        }
        await using var context = db.Open();
        await Assert.ThrowsAsync<RuntimeHistoryIntegrityException>(() => db.Store(context).ReadPageAsync(new("run", activityExecutionId: "other", includeDescendants: true)).AsTask());
    }

    [Fact]
    public async Task Deep_known_ancestry_cannot_escape_the_bounded_traversal_limit()
    {
        await using var db = await Database.Create();
        var facts = Enumerable.Range(0, 260).Select(index => new Fact("event-" + index, "n-" + index, Parent: index == 0 ? null : "n-" + (index - 1))).ToList();
        facts.Add(new("end", "n-259", "ActivityCompleted", Parent: "n-258")); await db.Append(facts.ToArray());
        await using var context = db.Open();
        await Assert.ThrowsAsync<RuntimeHistoryIntegrityException>(() => db.Store(context).ReadPageAsync(new("run", activityExecutionId: "other", includeDescendants: true, name: "ActivityCompleted")).AsTask());
    }

    [Fact]
    public async Task Unknown_recorded_event_semantics_remain_readable_without_being_reinterpreted()
    {
        await using var db = await Database.Create(); await db.Append(new Fact("future", "a", "FutureLifecycle", KnownTime: false));
        await using (var newer = db.Open())
        {
            var row = await newer.Set<RuntimeHistoryEventEntity>().SingleAsync(); var node = JsonNode.Parse(row.ContentJson)!;
            node["schemaVersion"] = 7; node["category"] = 500; node["authority"] = 500; row.Category = 500; row.ContentJson = node.ToJsonString(); await newer.SaveChangesAsync();
        }
        await using var context = db.Open(); var page = await db.Store(context).ReadPageAsync(new("run")); var item = Assert.Single(page.Items);
        Assert.Equal(7, item.SchemaVersion); Assert.Equal((RuntimeHistoryEventCategory)500, item.Category); Assert.Equal((RuntimeHistoryAuthority)500, item.Authority);
        Assert.False(item.OccurredAtKnown); Assert.Equal("FutureLifecycle", item.Name);
    }

    [Fact]
    public async Task Maintained_counts_cannot_contradict_the_actual_bounded_current_records()
    {
        await using var db = await Database.Create(); await db.Append(new Fact("created", "a", "BookmarkCreated", Mutation: Mutation("b", "a")));
        await using (var corrupt = db.Open())
        {
            var row = await corrupt.Set<RuntimeHistoryBookmarkCountEntity>().SingleAsync(item => item.ActivityExecutionId == null);
            var next = new Mapping(Chain).Row("scope", "run", new Mapping.BookmarkCount(null, 0, 0)); next.Revision = row.Revision + 1;
            corrupt.Entry(row).CurrentValues.SetValues(next); await corrupt.SaveChangesAsync();
        }
        await using var context = db.Open(); await Assert.ThrowsAsync<RuntimeHistoryIntegrityException>(() => db.Store(context).ListBookmarksAsync(new("run")).AsTask());
    }

    [Fact]
    public async Task Long_filtered_reads_remain_bounded_and_continue_after_an_empty_window()
    {
        await using var db = await Database.Create(); await db.Append(Enumerable.Range(0, 1005).Select(index => new Fact("e-" + index, "a-" + index)).ToArray());
        var commands = new Commands(); await using var context = db.Open(commands); var store = db.Store(context);
        var first = await store.ReadPageAsync(new("run", 500, search: "absent")); Assert.Empty(first.Items); Assert.InRange(first.ScannedCount, 1, 1004); Assert.NotNull(first.NextContinuationToken);
        var next = await store.ReadPageAsync(new("run", 500, first.NextContinuationToken, search: "absent"));
        Assert.Equal(1005, next.ScannedThroughSequence); Assert.Null(next.NextContinuationToken); Assert.Empty(next.Items);
        Assert.DoesNotContain(commands.Texts, sql => sql.Contains("COUNT(", StringComparison.OrdinalIgnoreCase) || sql.Contains("OFFSET", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class SnapshotGate : DbCommandInterceptor
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool used;
        public override async ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData data, DbDataReader result, CancellationToken token = default)
        {
            if (!used && command.CommandText.Contains(RuntimeHistoryProjectionSchema.HeadTable, StringComparison.Ordinal))
            { used = true; Entered.SetResult(); await Release.Task.WaitAsync(TimeSpan.FromSeconds(20), token); }
            return result;
        }
    }
}
