using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.UserTasks;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Recovery;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed class EfUserTaskStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Unknown_task_and_form_stamps_are_schema_skew_not_corrupt_content(bool form)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var context = new RuntimeSqliteDbContext(new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync(); await Executions(context).SaveAsync(Execution("run"));
        var journal = new EfUserTaskStore(context, new Accessor("tenant"));
        if (form)
        {
            Assert.True(await journal.TryCreateFormAsync(new("tenant", new("purchase", "1", "Purchase", UserTaskProtocol.BasicForms, []), Now, "author")));
            await context.UserTaskForms.ExecuteUpdateAsync(set => set.SetProperty(row => row.SchemaVersion, "999.0.0"));
        }
        else
        {
            Assert.True(await journal.TryWriteAsync(Task("run", "task"), null));
            await context.UserTasks.ExecuteUpdateAsync(set => set.SetProperty(row => row.SchemaVersion, "999.0.0"));
        }
        var skew = await Assert.ThrowsAsync<EfSchemaVersionSkewException>(async () =>
        {
            if (form) await journal.FindFormAsync("tenant", "purchase", "1");
            else await journal.FindAsync("tenant", "task");
        });
        Assert.Equal("UserTasks", skew.Family);
        Assert.Equal(["1.0.0"], skew.ReadableVersions);
    }

    [Fact]
    public async Task Durable_journal_CAS_history_and_form_snapshots_survive_a_new_context()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(connection).Options;
        await using (var context = new RuntimeSqliteDbContext(options))
        {
            await context.Database.EnsureCreatedAsync();
            await Executions(context).SaveAsync(Execution("run"));
            var store = new EfUserTaskStore(context, new Accessor("tenant"));
            Assert.True(await store.TryWriteAsync(Task("run", "a"), null));
            Assert.False(await store.TryWriteAsync(Task("run", "a"), null));
            var original = (await store.FindAsync("tenant", "a"))!;
            var claimed = original with { Status = UserTaskStatus.Claimed, ClaimantId = "alice", Revision = 2,
                History = original.History.Append(new("claimed", "alice", Now)).ToArray() };
            Assert.True(await store.TryWriteAsync(claimed, 1));
            Assert.False(await store.TryWriteAsync(original with { Revision = 2, ClaimantId = "bob" }, 1));
            var form = new UserTaskFormVersion("tenant", new("Purchase", "1", "Purchase", UserTaskProtocol.BasicForms, [new("AccountID", "Account", "text")]), Now, "author");
            Assert.True(await store.TryCreateFormAsync(form));
            Assert.False(await store.TryCreateFormAsync(form));
        }
        await using var restarted = new RuntimeSqliteDbContext(options);
        var journal = new EfUserTaskStore(restarted, new Accessor("tenant"));
        var task = await journal.FindAsync("tenant", "a");
        Assert.Equal("alice", task!.ClaimantId); Assert.Equal(2, task.Revision); Assert.Equal(2, task.History.Count);
        Assert.Equal("AccountID", Assert.Single((await journal.FindFormAsync("tenant", "Purchase", "1"))!.Form.Fields).Key);
        Assert.Null(await new EfUserTaskStore(restarted, new Accessor("other")).FindAsync("tenant", "a"));
    }

    [Fact]
    public async Task Query_filters_assignment_before_take_and_recovery_advances_over_pending_work()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var context = new RuntimeSqliteDbContext(new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync(); await Executions(context).SaveAsync(Execution("run"));
        var store = new EfUserTaskStore(context, new Accessor("tenant"));
        foreach (var task in new[] { Task("run", "a") with { AssigneeId = "bob" }, Task("run", "b"), Task("run", "c") })
            Assert.True(await store.TryWriteAsync(task, null));
        var first = await store.QueryAsync(new("tenant", "alice", new HashSet<string>(), "mine", 1));
        Assert.Equal("b", Assert.Single(first.Items).Id);
        var second = await store.QueryAsync(new("tenant", "alice", new HashSet<string>(), "mine", 1, first.Next));
        Assert.Equal("c", Assert.Single(second.Items).Id);
        Assert.Null(second.Next);
        var recovery = await store.RecoverAsync(1, null); Assert.Equal("a", Assert.Single(recovery.Items).Id);
        Assert.Equal("b", Assert.Single((await store.RecoverAsync(1, recovery.Next)).Items).Id);
    }

    [Fact]
    public async Task Journal_refuses_to_flush_unrelated_changes_or_attach_to_a_deleted_execution()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var context = new RuntimeSqliteDbContext(new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync();
        var store = new EfUserTaskStore(context, new Accessor("tenant"));
        Assert.Equal("unavailable", (await Assert.ThrowsAsync<UserTaskException>(() => store.TryWriteAsync(Task("missing", "task"), null).AsTask())).Code);
        var pending = new Entities.UserTaskFormEntity { Id = "pending", ScopeHash = "s", ScopeKey = "s", TenantHash = "t", TenantKey = "t", FormKey = "f", VersionKey = "v", ContentJson = "{}" };
        context.UserTaskForms.Add(pending);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.TryCreateFormAsync(new("tenant", new("f", "1", "F", UserTaskProtocol.BasicForms, []), Now, "author")).AsTask());
        Assert.Equal(EntityState.Added, context.Entry(pending).State);
    }

    [Theory]
    [InlineData("claim")]
    [InlineData("complete")]
    [InlineData("cancel-versus-complete")]
    public async Task Independently_scoped_writers_overlap_and_commit_one_aggregate_history_and_response(string scenario)
    {
        var directory = Directory.CreateTempSubdirectory("elsa-task-cas-");
        var options = new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite($"Data Source={Path.Combine(directory.FullName, "runtime.db")};Pooling=False").Options;
        try
        {
            await using (var setup = new RuntimeSqliteDbContext(options))
            {
                await setup.Database.EnsureCreatedAsync(); await Executions(setup).SaveAsync(Execution("run"));
                Assert.True(await new EfUserTaskStore(setup, new Accessor("tenant")).TryWriteAsync(Task("run", "task"), null));
            }
            var bothRead = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously); var reads = 0;
            async Task<bool> Race(string actor)
            {
                await using var context = new RuntimeSqliteDbContext(options);
                var store = new EfUserTaskStore(context, new Accessor("tenant")); var before = (await store.FindAsync("tenant", "task"))!;
                Assert.Equal(1, before.Revision);
                if (Interlocked.Increment(ref reads) == 2) bothRead.SetResult();
                await bothRead.Task.WaitAsync(TimeSpan.FromSeconds(10));
                var cancel = scenario == "cancel-versus-complete" && actor == "bob";
                var next = before with { Revision = 2,
                    Status = scenario == "claim" ? UserTaskStatus.Claimed : cancel ? UserTaskStatus.Cancelled : UserTaskStatus.CompletionPending,
                    ClaimantId = scenario == "claim" ? actor : null,
                    Decision = scenario == "claim" || cancel ? null : new("request-" + actor, actor, new(before.Id, "completion-" + actor, actor == "alice" ? "Approve" : "Reject", JsonSerializer.SerializeToElement(new { })), Now),
                    History = before.History.Append(new UserTaskHistory(scenario, actor, Now)).ToArray() };
                return await store.TryWriteAsync(next, before.Revision);
            }
            var outcomes = await System.Threading.Tasks.Task.WhenAll(Race("alice"), Race("bob"));
            Assert.Single(outcomes, success => success);
            await using var verification = new RuntimeSqliteDbContext(options);
            var winner = (await new EfUserTaskStore(verification, new Accessor("tenant")).FindAsync("tenant", "task"))!;
            Assert.Equal(2, winner.Revision); Assert.Equal(2, winner.History.Count);
            var winnerActor = winner.History[1].ActorId;
            if (scenario == "claim") Assert.Equal(winnerActor, winner.ClaimantId);
            else if (winner.Status == UserTaskStatus.CompletionPending) Assert.Equal(winnerActor, winner.Decision!.ActorId);
            else Assert.Null(winner.Decision);
        }
        finally { directory.Delete(true); }
    }

    [Fact]
    public async Task Nonterminal_task_retains_execution_evidence_and_terminal_acknowledgement_releases_it()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var context = new RuntimeSqliteDbContext(new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(connection).Options);
        await context.Database.EnsureCreatedAsync(); var executions = Executions(context);
        await executions.SaveAsync(Execution("run") with { Status = WorkflowExecutionStatus.Completed });
        var store = new EfUserTaskStore(context, new Accessor("tenant"));
        Assert.True(await store.TryWriteAsync(Task("run", "task"), null));
        Assert.False(await executions.DeleteAsync("run"));
        Assert.NotNull(await executions.FindAsync("run")); Assert.False(await executions.IsDeletionTombstonedAsync("run"));
        var task = (await store.FindAsync("tenant", "task"))!;
        Assert.True(await store.TryWriteAsync(task with { Status = UserTaskStatus.Completed, Revision = 2 }, 1));
        Assert.True(await executions.DeleteAsync("run"));
        Assert.True(await executions.IsDeletionTombstonedAsync("run"));
        Assert.Equal(UserTaskStatus.Completed, (await store.FindAsync("tenant", "task"))!.Status);
        await Assert.ThrowsAsync<UserTaskException>(() => store.TryWriteAsync(Task("run", "late"), null).AsTask());
    }

    [Fact]
    public async Task Retention_qualification_requires_the_same_physical_context_and_partition()
    {
        await using var first = new RuntimeSqliteDbContext(new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite("Data Source=:memory:").Options);
        await using var second = new RuntimeSqliteDbContext(new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite("Data Source=:memory:").Options);
        var boundary = new EfUserTaskPersistenceBoundary(); var journal = new EfUserTaskStore(first, new Accessor("tenant"));
        Assert.True(boundary.CanRetainCompletionEvidence(journal, Executions(first)));
        Assert.False(boundary.CanRetainCompletionEvidence(journal, Executions(second)));
        Assert.False(boundary.CanRetainCompletionEvidence(new EfUserTaskStore(first, new Accessor("other")), Executions(first)));
    }

    private static EfWorkflowExecutionStateStore Executions(RuntimeDbContext context) => new(context, new Accessor("tenant"),
        new HmacRuntimeRecoveryContinuationCodec(Options.Create(new RuntimeRecoveryContinuationOptions { SigningKey = "01234567890123456789012345678901" })));
    private static WorkflowExecutionState Execution(string id) => new(id, new("artifact", "definition", "version", "1", "hash"), WorkflowExecutionStatus.Running,
        null, Now, Now, Now, null, null, null, "tenant", new Dictionary<string, string>());
    private static UserTaskRecord Task(string execution, string id) => new(id, "tenant", execution, "invocation-" + id, "node", "operation", "Purchase", null, "alice", null, null,
        ["Approve", "Reject"], [], null, JsonSerializer.SerializeToElement(new { }), UserTaskStatus.Open, 1, Now, Now, [new("created", "workflow", Now)]);
    private sealed class Accessor(string scope) : IPersistenceAccessContextAccessor { public PersistenceAccessContext Current => PersistenceAccessContext.Scoped(new(scope)); }
}
