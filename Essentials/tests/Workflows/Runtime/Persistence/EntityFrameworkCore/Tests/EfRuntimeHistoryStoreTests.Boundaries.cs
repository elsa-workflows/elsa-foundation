using System.Data.Common;
using System.Transactions;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class EfRuntimeHistoryStoreTests
{
    [Theory]
    [InlineData("head")] [InlineData("page")] [InlineData("event")] [InlineData("event-details")]
    [InlineData("bookmark")] [InlineData("bookmark-details")] [InlineData("bookmarks")] [InlineData("occurrences")]
    public async Task Every_declared_read_rechecks_actual_access_after_a_pending_provider_result(string operation)
    {
        await using var db = await Database.Create(); await db.Append(new Fact("event", "a"), new("created", "a", "BookmarkCreated", Mutation: Mutation("b", "a")));
        var hook = new AfterHead(() => db.Access.Current = PersistenceAccessContext.Scoped(new("other")));
        await using var context = db.Open(hook);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Invoke(db.Store(context), operation));
        Assert.True(hook.Fired); Assert.Empty(context.ChangeTracker.Entries()); Assert.Null(context.Database.CurrentTransaction);
    }

    [Theory]
    [InlineData("head")] [InlineData("page")] [InlineData("event")] [InlineData("event-details")]
    [InlineData("bookmark")] [InlineData("bookmark-details")] [InlineData("bookmarks")] [InlineData("occurrences")]
    public async Task Missing_or_unavailable_results_also_retire_when_the_access_snapshot_is_replaced(string operation)
    {
        await using var db = await Database.Create(); // Native run exists but no history head yet.
        var hook = new AfterHead(() => db.Access.Current = PersistenceAccessContext.Scoped(new("scope")));
        await using var context = db.Open(hook);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Invoke(db.Store(context), operation));
        Assert.True(hook.Fired);
    }

    [Theory]
    [InlineData("global")] [InlineData("privileged")] [InlineData("across")]
    public async Task No_read_uses_global_privileged_or_cross_partition_access(string kind)
    {
        await using var db = await Database.Create(); var commands = new Commands(); await using var context = db.Open(commands);
        db.Access.Current = kind switch
        { "global" => PersistenceAccessContext.Global, "privileged" => PersistenceAccessContext.PrivilegedScoped(new("scope"), new("test")),
          _ => PersistenceAccessContext.PrivilegedAcrossScopes(new("test")) };
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.Store(context).FindHeadAsync("run").AsTask()); Assert.Empty(commands.Texts);
    }

    [Theory]
    [InlineData("head")] [InlineData("page")] [InlineData("event")] [InlineData("event-details")]
    [InlineData("bookmark")] [InlineData("bookmark-details")] [InlineData("bookmarks")] [InlineData("occurrences")]
    public async Task Actual_provider_failure_is_localized_with_original_cause_for_every_read(string operation)
    {
        await using var db = await Database.Create();
        await using (var corrupt = db.Open()) await corrupt.Database.ExecuteSqlRawAsync("DROP TABLE elsa_runtime_history_head");
        await using var context = db.Open();
        var failure = await Assert.ThrowsAsync<RuntimeHistoryUnavailableException>(() => Invoke(db.Store(context), operation));
        Assert.Equal("run", failure.WorkflowExecutionId); Assert.IsType<SqliteException>(failure.InnerException);
        Assert.NotEmpty(failure.Operation); Assert.Null(context.Database.CurrentTransaction); Assert.Empty(context.ChangeTracker.Entries());
    }

    [Theory]
    [InlineData("scope")] [InlineData("run")] [InlineData("limit")] [InlineData("access")]
    [InlineData("activity")] [InlineData("descendants")] [InlineData("name")] [InlineData("search")]
    public async Task Ordinary_cursor_binding_cannot_be_reused_for_another_filter_or_access_context(string change)
    {
        await using var db = await Database.Create(); await db.Append(new Fact("a", "a"), new("b", "b"), new("c", "c"));
        string token;
        await using (var first = db.Open()) token = (await db.Store(first).ReadPageAsync(new("run", 1, accessBinding: "allowed"))).NextContinuationToken!;
        var commands = new Commands(); await using var context = db.Open(commands);
        if (change == "scope") db.Access.Current = PersistenceAccessContext.Scoped(new("other"));
        var query = new RuntimeHistoryQuery(change == "run" ? "other" : "run", change == "limit" ? 2 : 1, token,
            activityExecutionId: change is "activity" or "descendants" ? "a" : null, includeDescendants: change == "descendants",
            name: change == "name" ? "ActivityStarted" : null, search: change == "search" ? "a" : null,
            accessBinding: change == "access" ? "changed" : "allowed");
        await Assert.ThrowsAsync<ArgumentException>(() => db.Store(context).ReadPageAsync(query).AsTask()); Assert.Empty(commands.Texts);
    }

    [Fact]
    public async Task Token_purposes_and_bookmark_status_filters_are_distinct()
    {
        await using var db = await Database.Create(); await db.Append(new Fact("a", "a"), new("b", "b"),
            new("created-1", "a", "BookmarkCreated", Mutation: Mutation("b-1", "a")), new("created-2", "a", "BookmarkCreated", Mutation: Mutation("b-2", "a")));
        await using var context = db.Open(); var store = db.Store(context);
        var events = await store.ReadPageAsync(new("run", 1)); var bookmarks = await store.ListBookmarksAsync(new("run", 1));
        await Assert.ThrowsAsync<ArgumentException>(() => store.ReadPageAsync(new("run", 1, bookmarks.NextContinuationToken)).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => store.ListBookmarksAsync(new("run", 1, events.NextContinuationToken)).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => store.ListBookmarksAsync(new("run", 1, bookmarks.NextContinuationToken, status: RuntimeHistoryBookmarkStatus.Active)).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => store.ReadOccurrencesAsync(new("run", 1, events.NextContinuationToken)).AsTask());
    }

    [Fact]
    public async Task Catch_up_from_a_missing_initial_head_can_observe_its_first_real_commit()
    {
        await using var db = await Database.Create(); string token;
        await using (var before = db.Open())
        { var page = await db.Store(before).ReadPageAsync(new("run", 2)); Assert.Null(page.Head); Assert.Empty(page.Items); token = page.CatchUpToken; }
        await db.Append(new Fact("first", "a")); await using var context = db.Open();
        var pageAfter = await db.Store(context).ReadPageAsync(new("run", 2, token)); Assert.Equal("first", Assert.Single(pageAfter.Items).HistoryEventId);
    }

    [Fact]
    public async Task Caller_transactions_and_dirty_state_are_preserved_and_requested_cancellation_is_not_normalized()
    {
        await using var db = await Database.Create(); await db.Append(new Fact("a", "a")); await using var context = db.Open(); var store = db.Store(context);
        await using (var transaction = await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable))
        { Assert.Equal(1, (await store.FindHeadAsync("run"))!.LastSequence); Assert.Same(transaction, context.Database.CurrentTransaction); await transaction.RollbackAsync(); }
        var row = await context.Set<RuntimeHistoryHeadEntity>().SingleAsync(); row.Revision++;
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.FindHeadAsync("run").AsTask()); Assert.Equal(EntityState.Modified, context.Entry(row).State);
        context.Entry(row).State = EntityState.Detached;
        using (var ambient = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.FindHeadAsync("run").AsTask());
        using var canceled = new CancellationTokenSource(); canceled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.FindHeadAsync("run", canceled.Token).AsTask()); Assert.Null(context.Database.CurrentTransaction);
    }

    [Fact]
    public async Task Cancellation_after_an_actual_provider_await_retires_without_clearing_caller_tracking()
    {
        await using var db = await Database.Create(); await db.Append(new Fact("a", "a")); using var canceled = new CancellationTokenSource();
        var hook = new AfterHead(() => canceled.Cancel()); await using var context = db.Open(hook);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => db.Store(context).ReadPageAsync(new("run"), canceled.Token).AsTask());
        Assert.True(hook.Fired); Assert.Null(context.Database.CurrentTransaction); Assert.Empty(context.ChangeTracker.Entries());
    }

    private static async Task<object?> Invoke(EfRuntimeHistoryStore store, string operation) => operation switch
    {
        "head" => await store.FindHeadAsync("run"), "page" => await store.ReadPageAsync(new("run")),
        "event" => await store.FindEventAsync("run", "event"), "event-details" => await store.FindEventDetailsAsync("run", "event"),
        "bookmark" => await store.FindBookmarkAsync("run", "b"), "bookmark-details" => await store.FindBookmarkDetailsAsync("run", "b"),
        "bookmarks" => await store.ListBookmarksAsync(new("run")), _ => await store.ReadOccurrencesAsync(new("run"))
    };
    private sealed class AfterHead(Action action) : DbCommandInterceptor
    {
        internal bool Fired;
        public override ValueTask<DbDataReader> ReaderExecutedAsync(DbCommand command, CommandExecutedEventData data, DbDataReader result, CancellationToken token = default)
        {
            if (!Fired && command.CommandText.Contains(RuntimeHistoryProjectionSchema.HeadTable, StringComparison.Ordinal)) { Fired = true; action(); }
            return ValueTask.FromResult(result);
        }
    }
}
