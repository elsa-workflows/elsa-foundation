using System.Text.Json;
using System.Text.Json.Nodes;
using System.Data;
using System.Data.Common;
using Elsa.Persistence.EntityFramework.Tests;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Exceptions;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class EfRuntimeCheckpointCommitStoreTests
{
    [Theory]
    [InlineData("head")] [InlineData("event")] [InlineData("bookmark")] [InlineData("events")] [InlineData("bookmarks")]
    [InlineData("record")] [InlineData("purge")]
    [Trait("Feature", "RunHistory")]
    public async Task History_provider_boundary_localizes_actual_sqlite_query_failures(string operation)
    {
        await using var db = await TestDatabase.CreateAsync();
        await SeedHistoryFailureBoundaryAsync(db);
        var failure = new HistoryQueryFailureProbe();
        await using var context = db.Open("tenant-a", failure);
        await context.Database.ExecuteSqlRawAsync("DROP TABLE elsa_runtime_history_head");
        var error = await Assert.ThrowsAsync<RuntimeHistoryEntityFrameworkPersistenceException>(() =>
            InvokeHistoryPortAsync(HistoryStore(context, new FixedAccessor("tenant-a")), operation));
        AssertHistoryFailure(error, operation);
        Assert.Equal(1, Assert.IsType<SqliteException>(error.InnerException).SqliteErrorCode);
        Assert.Same(failure.Cause, error.InnerException);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Theory]
    [InlineData("head", "begin")] [InlineData("record", "begin")] [InlineData("purge", "begin")]
    [InlineData("head", "commit")] [InlineData("record", "commit")] [InlineData("purge", "commit")]
    [Trait("Feature", "RunHistory")]
    public async Task History_provider_boundary_covers_owned_transaction_failures_without_extra_retries(string operation, string stage)
    {
        await using var db = await TestDatabase.CreateAsync();
        await SeedHistoryFailureBoundaryAsync(db);
        var cause = ProviderFailures.WrappedByExecutionStrategy(new ProviderFailures.SyntheticProviderException());
        var failure = new HistoryTransactionFailure(stage, cause);
        await using (var context = db.Open("tenant-a", failure))
        {
            var error = await Assert.ThrowsAsync<RuntimeHistoryEntityFrameworkPersistenceException>(() =>
                InvokeHistoryPortAsync(HistoryStore(context, new FixedAccessor("tenant-a")), operation));
            AssertHistoryFailure(error, operation); Assert.Same(cause, error.InnerException);
            Assert.Equal(1, failure.Failures); Assert.Empty(context.ChangeTracker.Entries());
        }
        await AssertHistoryFailureRolledBackAsync(db);
    }

    [Theory]
    [InlineData("head")] [InlineData("record")] [InlineData("purge")]
    [Trait("Feature", "RunHistory")]
    public async Task History_provider_boundary_covers_disposal_failure_without_claiming_commit_rollback(string operation)
    {
        await using var db = await TestDatabase.CreateAsync();
        await SeedHistoryFailureBoundaryAsync(db);
        var cause = new ProviderFailures.SyntheticProviderException();
        var failures = 0;
        var options = new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(db.Connection)
            .LogTo((eventId, _) => eventId.Id == RelationalEventId.TransactionDisposed.Id, _ =>
            {
                // Exercise the actual EF transaction-disposal path after its successful commit. No
                // interceptor callback exists for DisposeAsync; throw once from its disposal diagnostic.
                if (Interlocked.Increment(ref failures) == 1) throw cause;
            }).Options;
        await using (var context = new RuntimeSqliteDbContext(options))
        {
            var error = await Assert.ThrowsAsync<RuntimeHistoryEntityFrameworkPersistenceException>(() =>
                InvokeHistoryPortAsync(HistoryStore(context, new FixedAccessor("tenant-a")), operation));
            AssertHistoryFailure(error, operation); Assert.Same(cause, error.InnerException);
            Assert.Empty(context.ChangeTracker.Entries());
        }
        await using var restarted = db.Open("tenant-a");
        var reader = HistoryStore(restarted, new FixedAccessor("tenant-a"));
        Assert.Equal(operation == "record" ? 2 : 1, (await reader.FindHeadAsync("workflow-a"))!.LastSequence);
        if (operation == "record")
        {
            Assert.True((await reader.RecordAsync(HistoryBoundaryObservation())).Recorded);
            Assert.Equal(2, (await reader.FindHeadAsync("workflow-a"))!.LastSequence);
        }
        if (operation == "purge") Assert.Equal(1, await reader.PurgeThroughAsync("workflow-a", 1));
        Assert.Single(await restarted.RuntimeCheckpointCommits.ToArrayAsync());
    }

    [Theory]
    [InlineData("record")] [InlineData("purge")]
    [Trait("Feature", "RunHistory")]
    public async Task History_provider_boundary_preserves_wrapped_save_cause_and_atomic_rollback(string operation)
    {
        await using var db = await TestDatabase.CreateAsync();
        await SeedHistoryFailureBoundaryAsync(db);
        var cause = ProviderFailures.WrappedSaveFailure(new ProviderFailures.SyntheticProviderException());
        var save = new ProviderFailures.FailingSaveInterceptor(() => cause);
        await using (var context = db.Open("tenant-a", save))
        {
            var error = await Assert.ThrowsAsync<RuntimeHistoryEntityFrameworkPersistenceException>(() =>
                InvokeHistoryPortAsync(HistoryStore(context, new FixedAccessor("tenant-a")), operation));
            AssertHistoryFailure(error, operation); Assert.Same(cause, error.InnerException);
            Assert.Equal(1, save.Attempts); Assert.Empty(context.ChangeTracker.Entries());
        }
        // Purge executed its content tombstone before SaveChanges failed; observation staged its new
        // event and head. Neither failure may leave that earlier work or an extra physical checkpoint.
        await AssertHistoryFailureRolledBackAsync(db);
    }

    [Theory]
    [InlineData("head")] [InlineData("record")] [InlineData("purge")]
    [Trait("Feature", "RunHistory")]
    public async Task History_provider_boundary_preserves_cancellation_and_hard_integrity(string operation)
    {
        await using var db = await TestDatabase.CreateAsync();
        await SeedHistoryFailureBoundaryAsync(db);
        var cancellation = new OperationCanceledException("Unrequested provider cancellation remains cancellation.");
        await using (var context = db.Open("tenant-a", new ProviderFailures.FailingReadInterceptor(() => cancellation)))
        {
            Assert.Same(cancellation, await Assert.ThrowsAsync<OperationCanceledException>(() =>
                InvokeHistoryPortAsync(HistoryStore(context, new FixedAccessor("tenant-a")), operation)));
            Assert.Empty(context.ChangeTracker.Entries());
        }
        using var stop = new CancellationTokenSource(); stop.Cancel();
        await using (var context = db.Open("tenant-a"))
        {
            var cancelled = await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                InvokeHistoryPortAsync(HistoryStore(context, new FixedAccessor("tenant-a")), operation, stop.Token));
            Assert.Equal(stop.Token, cancelled.CancellationToken);
        }
        string originalContent;
        await using (var stored = db.Open("tenant-a"))
            originalContent = await stored.RuntimeHistoryHeads.AsNoTracking().Select(row => row.ContentJson).SingleAsync();
        using (var stored = JsonDocument.Parse(originalContent))
            Assert.Equal(JsonValueKind.Number, stored.RootElement.GetProperty("policy").GetProperty("version").ValueKind);
        var typed = JsonNode.Parse(originalContent)!;
        typed["policy"]!["version"] = "malformed-policy-version";
        foreach (var corruption in new[] { (Content: "[malformed", Typed: false), (Content: typed.ToJsonString(), Typed: true) })
        {
            await using (var corrupt = db.Open("tenant-a"))
                await corrupt.RuntimeHistoryHeads.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ContentJson, corruption.Content));
            await using var invalid = db.Open("tenant-a");
            var integrity = await Assert.ThrowsAsync<RuntimeHistoryIntegrityException>(() =>
                InvokeHistoryPortAsync(HistoryStore(invalid, new FixedAccessor("tenant-a")), operation));
            var parse = Assert.IsType<JsonException>(integrity.InnerException);
            Assert.False(string.IsNullOrWhiteSpace(parse.Path));
            Assert.False(string.IsNullOrWhiteSpace(parse.StackTrace));
            if (corruption.Typed)
            {
                Assert.Equal("$.policy.version", parse.Path);
                Assert.Equal(0, parse.LineNumber);
                Assert.True(parse.BytePositionInLine > 0);
            }
            Assert.DoesNotContain("malformed-policy-version", integrity.Message);
            Assert.Empty(invalid.ChangeTracker.Entries());
        }
    }

    [Fact]
    [Trait("Feature", "RunHistory")]
    public async Task History_provider_boundary_keeps_argument_policy_and_dirty_context_refusals_hard()
    {
        await using var db = await TestDatabase.CreateAsync();
        await SeedHistoryFailureBoundaryAsync(db);
        await using var context = db.Open("tenant-a");
        var history = HistoryStore(context, new FixedAccessor("tenant-a"));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => history.PurgeThroughAsync("workflow-a", -1).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => history.ReadPageAsync(new("workflow-a", continuationToken: "invalid")).AsTask());
        context.SchedulerStates.Add(SchedulerRow("tenant-a", "workflow-a"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => history.FindHeadAsync("workflow-a").AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => history.RecordAsync(HistoryBoundaryObservation()).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => history.PurgeThroughAsync("workflow-a", 1).AsTask());
        Assert.True(context.ChangeTracker.HasChanges()); Assert.Single(context.ChangeTracker.Entries());
        context.ChangeTracker.Clear();
        var conflicting = HistoryBoundaryObservation() with
        { Policy = new("conflicting-policy", 1, true, HistoryPolicy.SupportedKinds) };
        await Assert.ThrowsAsync<InvalidOperationException>(() => history.RecordAsync(conflicting).AsTask());
        Assert.Empty(context.ChangeTracker.Entries());
        await AssertHistoryFailureRolledBackAsync(db);
    }

    [Theory]
    [InlineData(1, true)] [InlineData(3, false)]
    [Trait("Feature", "RunHistory")]
    public async Task History_provider_boundary_keeps_observation_head_conflict_retries_narrow_and_bounded(int misses, bool succeeds)
    {
        await using var db = await TestDatabase.CreateAsync();
        await SeedHistoryFailureBoundaryAsync(db);
        var conflict = new HistoryObservationHeadConflict(misses);
        await using (var context = db.Open("tenant-a", conflict))
        {
            var reader = HistoryStore(context, new FixedAccessor("tenant-a"));
            if (succeeds) Assert.True((await reader.RecordAsync(HistoryBoundaryObservation())).Recorded);
            else
            {
                var error = await Assert.ThrowsAsync<RuntimeHistoryEntityFrameworkPersistenceException>(() =>
                    reader.RecordAsync(HistoryBoundaryObservation()).AsTask());
                AssertHistoryFailure(error, "record"); Assert.IsType<DbUpdateConcurrencyException>(error.InnerException);
            }
            Assert.Equal(succeeds ? misses + 1 : 3, conflict.Attempts); Assert.Empty(context.ChangeTracker.Entries());
        }
        await using var restarted = db.Open("tenant-a");
        Assert.Equal(succeeds ? 2 : 1, (await HistoryStore(restarted, new FixedAccessor("tenant-a")).FindHeadAsync("workflow-a"))!.LastSequence);
        Assert.Equal(succeeds ? 2 : 1, await restarted.RuntimeHistoryEvents.CountAsync());
        Assert.Single(await restarted.RuntimeCheckpointCommits.ToArrayAsync());
    }

    private static async Task SeedHistoryFailureBoundaryAsync(TestDatabase database)
    {
        await using var context = database.Open("tenant-a");
        await SeedHistoryRunAsync(context);
        await new EfRuntimeCheckpointCommitStore(context, new FixedAccessor("tenant-a")).CommitAsync(HistoryCommit("provider-boundary", true,
            [HistoryCandidate("checkpoint-provider-boundary", 0, "WorkflowStarted")]), Decision());
    }

    private static RuntimeHistoryObservation HistoryBoundaryObservation() => new(HistoryPolicy,
        new RuntimeHistoryEvent("provider-boundary-observation", "workflow-a", 0, RuntimeHistoryEventCategory.DeliveryObservation,
            "StimulusReceived", OccurredAt.AddMinutes(1)) { Authority = RuntimeHistoryAuthority.DeliveryObservation });

    private static async Task InvokeHistoryPortAsync(EfRuntimeHistoryStore store, string operation, CancellationToken token = default)
    {
        switch (operation)
        {
            case "head": await store.FindHeadAsync("workflow-a", token); break;
            case "event": await store.FindEventAsync("workflow-a", "event", token); break;
            case "bookmark": await store.FindBookmarkAsync("workflow-a", "bookmark", token); break;
            case "events": await store.ReadPageAsync(new("workflow-a"), token); break;
            case "bookmarks": await store.ListBookmarksAsync(new("workflow-a"), token); break;
            case "record": await store.RecordAsync(HistoryBoundaryObservation(), token); break;
            case "purge": await store.PurgeThroughAsync("workflow-a", 1, token); break;
            default: throw new ArgumentOutOfRangeException(nameof(operation));
        }
    }

    private static void AssertHistoryFailure(RuntimeHistoryEntityFrameworkPersistenceException error, string operation)
    {
        Assert.IsAssignableFrom<IOException>(error);
        Assert.Equal("tenant-a", error.Scope); Assert.Equal("workflow-a", error.WorkflowExecutionId);
        Assert.Contains("tenant-a", error.Message); Assert.Contains("workflow-a", error.Message);
        Assert.Equal(operation switch
        {
            "head" => "reading the history head", "event" => "reading an exact history event", "bookmark" => "reading an exact history bookmark",
            "events" => "reading a history page", "bookmarks" => "reading a bookmark history page",
            "record" => "recording a delivery observation", "purge" => "purging retained history", _ => throw new ArgumentOutOfRangeException(nameof(operation))
        }, error.Operation);
    }

    private static async Task AssertHistoryFailureRolledBackAsync(TestDatabase database)
    {
        await using var verify = database.Open("tenant-a");
        var head = (await HistoryStore(verify, new FixedAccessor("tenant-a")).FindHeadAsync("workflow-a"))!;
        Assert.Equal(1, head.LastSequence); Assert.Equal(0, head.PurgedThroughSequence);
        var item = Assert.Single(await verify.RuntimeHistoryEvents.ToArrayAsync());
        Assert.False(item.IsPurged); Assert.False(string.IsNullOrEmpty(item.ContentJson));
        Assert.Single(await verify.RuntimeCheckpointCommits.ToArrayAsync());
    }

    private sealed class HistoryQueryFailureProbe : DbCommandInterceptor
    {
        public Exception? Cause { get; private set; }
        public override Task CommandFailedAsync(DbCommand command, CommandErrorEventData eventData, CancellationToken cancellationToken = default)
        { Cause = eventData.Exception; return Task.CompletedTask; }
    }

    private sealed class HistoryTransactionFailure(string stage, Exception cause) : DbTransactionInterceptor
    {
        public int Failures { get; private set; }
        public override ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(DbConnection connection, TransactionStartingEventData eventData,
            InterceptionResult<DbTransaction> result, CancellationToken cancellationToken = default)
        { if (stage == "begin") { Failures++; throw cause; } return ValueTask.FromResult(result); }
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(DbTransaction transaction, TransactionEventData eventData,
            InterceptionResult result, CancellationToken cancellationToken = default)
        { if (stage == "commit") { Failures++; throw cause; } return ValueTask.FromResult(result); }
    }

    private sealed class HistoryObservationHeadConflict(int misses) : DbCommandInterceptor
    {
        public int Attempts { get; private set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.StartsWith("UPDATE", StringComparison.OrdinalIgnoreCase) && command.CommandText.Contains("elsa_runtime_history_head", StringComparison.Ordinal))
            {
                Attempts++;
                if (Attempts <= misses) return ValueTask.FromResult(InterceptionResult<DbDataReader>.SuppressWithResult(new DataTable().CreateDataReader()));
            }
            return ValueTask.FromResult(result);
        }
    }
}
