using System.Data.Common;
using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Configuration;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Executables;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores.EfRuntimeOperationalStoreSupport;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed class EfRuntimeModelCheckpointTests
{
    [Theory]
    [InlineData("state")]
    [InlineData("input")]
    [InlineData("page")]
    public async Task Model_read_provider_failures_retain_context_and_cancellation(string operation)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var context = new RuntimeSqliteDbContext(new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(connection).Options);
        var reader = Reader(context);
        async Task Read(CancellationToken token = default)
        {
            if (operation == "state") await reader.FindAsync("requested-execution", token);
            else if (operation == "input") await reader.FindInputAsync("requested-execution", "requested-input", token);
            else await reader.ListPageAsync(new(), token);
        }
        var error = await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() => Read());
        Assert.IsType<SqliteException>(error.InnerException);
        Assert.Contains("tenant-a", error.Message);
        if (operation != "page") Assert.Contains("requested-execution", error.Message);
        if (operation == "input") Assert.Contains("requested-input", error.Message);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Read(cancellation.Token));
    }

    private static readonly DateTimeOffset Now = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);
    private static readonly RuntimeExecutionProtocol Protocol = new("test.model", 1);
    private static readonly WorkflowExecutableIdentity Pin = CreatePin();
    private string materialIncarnation = null!;
    private static readonly RuntimeExecutionLease Lease = new("lease", "execution", "owner", Now, Now.AddMinutes(10), 1);
    private static readonly RuntimeCheckpointPersistenceDecision Immediate = new(RuntimeCheckpointPersistenceMode.Immediate);

    [Fact]
    public async Task Native_admission_compares_the_exact_model_decision_without_rewriting_it()
    {
        await using var database = await CreateDatabaseAsync();
        var admission = await PrepareAdmissionAsync(database);
        string before;
        await using (var context = database.Open())
        {
            before = (await context.Set<RuntimeModelExecutionEntity>().SingleAsync()).ContentJson;
            await Store(context).CommitAsync(admission, Immediate);
        }
        await using var restarted = database.Open();
        Assert.Equal(before, (await restarted.Set<RuntimeModelExecutionEntity>().SingleAsync()).ContentJson);
        Assert.Equal(1, (await Reader(restarted).FindAsync("execution"))!.Revision);
        Assert.Equal(WorkflowExecutionStatus.Running,
            (await new EfWorkflowExecutionStateStore(restarted, new Access("tenant-a"), new Codec()).FindAsync("native-worker"))!.Status);
        Assert.Single(await restarted.RuntimePostCommitOutbox.ToArrayAsync());
        Assert.Equal(2, await restarted.RuntimeCheckpointCommits.CountAsync());
        Assert.Single(await restarted.Set<RuntimeModelInputEntity>().ToArrayAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_changed_model_decision_cannot_admit_native_work(bool changeWithoutRevision)
    {
        await using var database = await CreateDatabaseAsync();
        var admission = await PrepareAdmissionAsync(database);
        await using (var context = database.Open())
        {
            if (changeWithoutRevision)
            {
                var row = await context.Set<RuntimeModelExecutionEntity>().SingleAsync();
                row.ContentJson = row.ContentJson.Replace("human-wait", "cancelled", StringComparison.Ordinal);
                await context.SaveChangesAsync();
            }
            else await Store(context).CommitAsync(Commit(2, RuntimeModelLifecycle.TerminalSettled), Immediate);
        }
        await using (var context = database.Open())
            Assert.IsType<DbUpdateConcurrencyException>((await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() =>
                Store(context).CommitAsync(admission, Immediate).AsTask())).InnerException);
        await using var restarted = database.Open();
        Assert.Empty(await restarted.WorkflowExecutionStates.ToArrayAsync());
        Assert.Empty(await restarted.RuntimePostCommitOutbox.ToArrayAsync());
        Assert.False(await restarted.RuntimeCheckpointCommits.AnyAsync(row => row.CommitId == Encode(admission.CommitId)));
    }

    [Fact]
    public async Task Native_admission_still_compares_the_physical_model_revision_after_its_read()
    {
        await using var database = await CreateDatabaseAsync();
        var admission = await PrepareAdmissionAsync(database);
        var advance = new AdvanceBeforeModelCompare();
        await using (var context = database.Open(advance))
            Assert.IsType<DbUpdateConcurrencyException>((await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() =>
                Store(context).CommitAsync(admission, Immediate).AsTask())).InnerException);
        Assert.True(advance.Changed);
        await using var restarted = database.Open();
        Assert.Equal(1, (await Reader(restarted).FindAsync("execution"))!.Revision);
        Assert.Empty(await restarted.WorkflowExecutionStates.ToArrayAsync());
        Assert.Empty(await restarted.RuntimePostCommitOutbox.ToArrayAsync());
        Assert.Single(await restarted.RuntimeCheckpointCommits.ToArrayAsync());
    }

    [Fact]
    public async Task Failed_native_admission_marker_rolls_back_native_work_outbox_and_the_model_compare()
    {
        await using var database = await CreateDatabaseAsync();
        var admission = await PrepareAdmissionAsync(database);
        await using (var context = database.Open(new FailMarker()))
            Assert.IsType<DbUpdateException>((await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() =>
                Store(context).CommitAsync(admission, Immediate).AsTask())).InnerException);
        await using var restarted = database.Open();
        Assert.Equal(1, (await Reader(restarted).FindAsync("execution"))!.Revision);
        Assert.Empty(await restarted.WorkflowExecutionStates.ToArrayAsync());
        Assert.Empty(await restarted.RuntimePostCommitOutbox.ToArrayAsync());
        Assert.Single(await restarted.RuntimeCheckpointCommits.ToArrayAsync());
    }

    [Fact]
    public async Task Native_admission_replay_precedes_later_model_progress()
    {
        await using var database = await CreateDatabaseAsync();
        var admission = await PrepareAdmissionAsync(database);
        await using (var context = database.Open()) await Store(context).CommitAsync(admission, Immediate);
        await using (var context = database.Open()) await Store(context).CommitAsync(Commit(2), Immediate);
        await using (var context = database.Open()) await Store(context).CommitAsync(admission with
            { ExpectedModelExecution = await Reader(context).FindAsync("execution") }, Immediate);
        await using var restarted = database.Open();
        Assert.Equal(2, (await Reader(restarted).FindAsync("execution"))!.Revision);
        Assert.Single(await restarted.WorkflowExecutionStates.ToArrayAsync());
        Assert.Single(await restarted.RuntimePostCommitOutbox.ToArrayAsync());
        Assert.Equal(3, await restarted.RuntimeCheckpointCommits.CountAsync());
    }

    private async Task<RuntimeCheckpointCommit> PrepareAdmissionAsync(Database database)
    {
        await using var context = database.Open();
        await Store(context).CommitAsync(Commit(1), Immediate);
        var expected = (await Reader(context).FindAsync("execution"))!;
        var lease = new RuntimeExecutionLease("worker-lease", "native-worker", "owner", Now, Now.AddMinutes(10), 1);
        await new EfExecutionLivenessStateStore(context, new Access("tenant-a"), new Codec())
            .SaveAsync(new("ownership:native-worker", "native-worker", lease, null, null, null));
        var pin = new WorkflowExecutableIdentity("native-artifact", "worker", "worker-v1", "1.0.0", "worker-hash");
        var at = Now.AddSeconds(3);
        var workflow = new WorkflowExecutionState("native-worker", pin, WorkflowExecutionStatus.Running,
            null, at, at, at, null, null, null, expected.TenantId, new Dictionary<string, string>())
            { Partition = new(expected.Scope.Value), Authority = expected.Authority };
        var intent = new RuntimePostCommitIntent("start-root", workflow.WorkflowExecutionId, "test.start-root", at, null,
            "start-root", JsonSerializer.SerializeToElement(new { node = "root" }));
        var outbox = new RuntimePostCommitOutboxItem("start-root", intent, RuntimePostCommitOutboxStatus.Pending, at, at);
        return new("native-admission", new("native-admission", RuntimeCheckpointNames.WorkflowStarted,
                workflow.WorkflowExecutionId, at, [], new Dictionary<string, string>()),
            new(new(workflow.WorkflowExecutionId, RuntimeStateChangeOperation.Upsert, workflow, new Dictionary<string, string>()),
                null, [], [], [], [], [], postCommitOutbox:
                [new(outbox.OutboxItemId, RuntimeStateChangeOperation.Upsert, outbox, new Dictionary<string, string>())]),
            [intent], new Dictionary<string, string>())
            { ExpectedFence = lease.ToFence(), ExpectedModelExecution = expected };
    }

    [Fact]
    public async Task Model_owned_native_header_requires_admission_and_cannot_hide_in_the_legacy_family()
    {
        await using var database = await CreateDatabaseAsync();
        var admission = WithOrigin(await PrepareAdmissionAsync(database));
        await using (var context = database.Open())
        {
            await Assert.ThrowsAsync<NotSupportedException>(() => Store(context)
                .CommitAsync(admission with { ExpectedModelExecution = null }, Immediate).AsTask());
            Assert.Empty(await context.WorkflowExecutionStates.ToArrayAsync());
            await Store(context).CommitAsync(admission, Immediate);
            var row = await context.WorkflowExecutionStates.AsNoTracking().SingleAsync();
            Assert.Equal("2.0.0", row.SchemaVersion);
            var worker = await new EfWorkflowExecutionStateStore(context, new Access("tenant-a"), new Codec()).FindAsync("native-worker");
            Assert.True(admission.StateChanges.WorkflowExecution!.State.ModelWorkOrigin!.IsEquivalentTo(worker!.ModelWorkOrigin));
            await context.WorkflowExecutionStates.ExecuteUpdateAsync(set => set.SetProperty(state => state.SchemaVersion, "1.0.0"));
        }
        await using var restarted = database.Open();
        await Assert.ThrowsAsync<InvalidDataException>(() => new EfWorkflowExecutionStateStore(restarted, new Access("tenant-a"), new Codec())
            .FindAsync("native-worker").AsTask());
    }

    [Fact]
    public async Task Model_owned_native_work_cannot_drop_its_address_or_terminal_result_responsibility()
    {
        await using var database = await CreateDatabaseAsync();
        var admission = WithOrigin(await PrepareAdmissionAsync(database));
        await using (var context = database.Open()) await Store(context).CommitAsync(admission, Immediate);
        var worker = admission.StateChanges.WorkflowExecution!.State;
        var terminal = worker with { Status = WorkflowExecutionStatus.Completed, UpdatedAt = Now.AddSeconds(4), CompletedAt = Now.AddSeconds(4) };
        await using (var context = database.Open())
        {
            var executions = new EfWorkflowExecutionStateStore(context, new Access("tenant-a"), new Codec());
            await Assert.ThrowsAsync<InvalidOperationException>(() => executions.SaveAsync(worker with { ModelWorkOrigin = null }).AsTask());
            await Assert.ThrowsAsync<NotSupportedException>(() => executions.SaveAsync(terminal).AsTask());
        }
        var finished = new RuntimeCheckpointCommit("worker-complete", new("worker-complete", RuntimeCheckpointNames.WorkflowCompleted,
                "native-worker", Now.AddSeconds(4), [], new Dictionary<string, string>()),
            new(new("native-worker", RuntimeStateChangeOperation.Upsert, terminal, new Dictionary<string, string>()), null, [], [], [], [], []),
            [], new Dictionary<string, string>()) { ExpectedFence = admission.ExpectedFence };
        await using (var context = database.Open())
            await Assert.ThrowsAsync<NotSupportedException>(() => Store(context).CommitAsync(finished, Immediate).AsTask());
        await using var restarted = database.Open();
        var retained = (await new EfWorkflowExecutionStateStore(restarted, new Access("tenant-a"), new Codec()).FindAsync("native-worker"))!;
        Assert.Equal(WorkflowExecutionStatus.Running, retained.Status);
        Assert.True(worker.ModelWorkOrigin!.IsEquivalentTo(retained.ModelWorkOrigin));
        Assert.Equal(2, await restarted.RuntimeCheckpointCommits.CountAsync());
        Assert.Single(await restarted.RuntimePostCommitOutbox.ToArrayAsync());
    }

    [Theory]
    [InlineData("folded", true)]
    [InlineData("intent-only", false)]
    [InlineData("foreign-source-scope", false)]
    public async Task Model_worker_terminal_requires_materialized_delivery_even_after_a_balanced_fold(string shape, bool accepted)
    {
        await using var database = await CreateDatabaseAsync();
        var admission = WithOrigin(await PrepareAdmissionAsync(database));
        await using (var context = database.Open()) await Store(context).CommitAsync(admission, Immediate);
        var worker = admission.StateChanges.WorkflowExecution!.State;
        var origin = worker.ModelWorkOrigin!;
        var at = Now.AddSeconds(4);
        var intent = new RuntimePostCommitIntent("result", worker.WorkflowExecutionId, origin.ResultIntentKind, at,
            null, "result", JsonSerializer.SerializeToElement(new { origin, status = WorkflowExecutionStatus.Completed }));
        var outbox = new RuntimePostCommitOutboxItem("worker-complete:result", intent, RuntimePostCommitOutboxStatus.Pending,
            at, at, null, 0, null, null, null, null, null, 0, null, null,
            shape == "foreign-source-scope" ? new PersistenceScope("foreign") : origin.Scope, null);
        var terminal = worker with { Status = WorkflowExecutionStatus.Completed, UpdatedAt = at, CompletedAt = at };
        var finished = new RuntimeCheckpointCommit("worker-complete", new("worker-complete", RuntimeCheckpointNames.WorkflowCompleted,
                worker.WorkflowExecutionId, at, [], new Dictionary<string, string>()),
            new(new(worker.WorkflowExecutionId, RuntimeStateChangeOperation.Upsert, terminal, new Dictionary<string, string>()),
                null, [], [], [], [], [], postCommitOutbox: shape == "intent-only" ? [] :
                [new(outbox.OutboxItemId, RuntimeStateChangeOperation.Upsert, outbox, new Dictionary<string, string>())]),
            shape == "intent-only" ? [intent] : [], new Dictionary<string, string>()) { ExpectedFence = admission.ExpectedFence };
        await using (var context = database.Open())
        {
            if (accepted) await Store(context).CommitAsync(finished, Immediate);
            else await Assert.ThrowsAsync<NotSupportedException>(() => Store(context).CommitAsync(finished, Immediate).AsTask());
        }
        await using var restarted = database.Open();
        var retained = (await new EfWorkflowExecutionStateStore(restarted, new Access("tenant-a"), new Codec())
            .FindAsync(worker.WorkflowExecutionId))!;
        Assert.Equal(accepted ? WorkflowExecutionStatus.Completed : WorkflowExecutionStatus.Running, retained.Status);
        Assert.Equal(accepted ? 2 : 1, await restarted.RuntimePostCommitOutbox.CountAsync());
    }

    private static RuntimeCheckpointCommit WithOrigin(RuntimeCheckpointCommit admission)
    {
        var model = admission.ExpectedModelExecution!;
        var native = admission.StateChanges.WorkflowExecution!;
        var origin = new RuntimeModelNativeWorkOrigin(1, model.Scope, model.ExecutionId, model.Protocol, model.Executable,
            model.MaterialIncarnationId, "service", "invocation", "attempt", native.State.PinnedExecutable, native.State.WorkflowExecutionId,
            "source-commit:start", "start", "test.native-result", "result-input");
        return admission with { StateChanges = new(native with { State = native.State with { ModelWorkOrigin = origin } },
            null, [], [], [], [], [], postCommitOutbox: admission.StateChanges.PostCommitOutbox) };
    }

    [Fact]
    public async Task Correcting_a_premature_settlement_header_retains_the_exact_terminal_result_and_never_reopens_progression()
    {
        await using var database = await CreateDatabaseAsync();
        await using (var context = database.Open())
            await Store(context).CommitAsync(Commit(1, RuntimeModelLifecycle.TerminalSettled), Immediate);
        await using (var context = database.Open())
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => Store(context).CommitAsync(Commit(2), Immediate).AsTask());
            var commit = Commit(2, RuntimeModelLifecycle.TerminalPendingSettlement);
            var state = commit.ModelState!.State;
            var replacement = new RuntimeModelExecutionState(state.FormatVersion, state.Scope, state.ExecutionId, state.TenantId,
                state.Protocol, state.Executable, state.MaterialIncarnationId, state.Revision, state.Lifecycle, state.Authority,
                state.CreatedAt, state.UpdatedAt, state.Payload, JsonSerializer.SerializeToElement(new { outcome = "replaced" }));
            await Assert.ThrowsAsync<InvalidOperationException>(() => Store(context).CommitAsync(commit with
                { ModelState = new(commit.ModelState.ExpectedRevision, replacement, commit.ModelState.Input) }, Immediate).AsTask());
        }
        await using (var context = database.Open())
            await Store(context).CommitAsync(Commit(2, RuntimeModelLifecycle.TerminalPendingSettlement), Immediate);
        await using (var restarted = database.Open())
        {
            Assert.Equal(RuntimeModelLifecycle.TerminalPendingSettlement, (await Reader(restarted).FindAsync("execution"))!.Lifecycle);
            await Assert.ThrowsAsync<InvalidOperationException>(() => Store(restarted).CommitAsync(Commit(3), Immediate).AsTask());
        }
        await using (var context = database.Open())
            await Store(context).CommitAsync(Commit(3, RuntimeModelLifecycle.TerminalSettled), Immediate);
        await using var final = database.Open();
        Assert.Equal(RuntimeModelLifecycle.TerminalSettled, (await Reader(final).FindAsync("execution"))!.Lifecycle);
        Assert.Equal("completed", (await Reader(final).FindAsync("execution"))!.TerminalResult!.Value.GetProperty("outcome").GetString());
    }

    [Theory]
    [InlineData(RuntimeModelLifecycle.Running)]
    [InlineData(RuntimeModelLifecycle.TerminalSettled)]
    public async Task Retained_input_records_its_receipt_without_rewriting_execution_state(RuntimeModelLifecycle lifecycle)
    {
        await using var database = await CreateDatabaseAsync();
        await using (var context = database.Open()) await Store(context).CommitAsync(Commit(1, lifecycle), Immediate);
        await using (var context = database.Open())
        {
            var before = await context.Set<RuntimeModelExecutionEntity>().AsNoTracking().SingleAsync();
            await Store(context).CommitAsync(Retained(1, lifecycle), Immediate);
            var after = await context.Set<RuntimeModelExecutionEntity>().AsNoTracking().SingleAsync();
            Assert.Equal(before.ContentJson, after.ContentJson);
            Assert.Equal(before.Revision, after.Revision);
            Assert.Equal(before.SchemaVersion, after.SchemaVersion);
            Assert.Equal(1, (await Reader(context).FindInputAsync("execution", "obsolete"))!.AcceptedRevision);
            Assert.Equal(2, await context.RuntimeCheckpointCommits.CountAsync());
        }
        await using var restarted = database.Open();
        Assert.Equal(lifecycle, (await Reader(restarted).FindAsync("execution"))!.Lifecycle);
        Assert.NotNull(await Reader(restarted).FindInputAsync("execution", "obsolete"));
    }

    [Theory]
    [InlineData("payload")]
    [InlineData("timestamp")]
    [InlineData("terminal-result")]
    public async Task An_unchanged_revision_cannot_smuggle_a_state_mutation(string field)
    {
        await using var database = await CreateDatabaseAsync();
        await using var context = database.Open();
        await Store(context).CommitAsync(Commit(1, RuntimeModelLifecycle.TerminalSettled), Immediate);
        var commit = Retained(1, RuntimeModelLifecycle.TerminalSettled);
        var state = commit.ModelState!.State;
        var changed = new RuntimeModelExecutionState(state.FormatVersion, state.Scope, state.ExecutionId, state.TenantId, state.Protocol,
            state.Executable, state.MaterialIncarnationId, state.Revision, state.Lifecycle, state.Authority, state.CreatedAt,
            field == "timestamp" ? state.UpdatedAt.AddTicks(1) : state.UpdatedAt,
            field == "payload" ? JsonSerializer.SerializeToElement(new { tokens = new[] { "injected" } }) : state.Payload,
            field == "terminal-result" ? JsonSerializer.SerializeToElement(new { outcome = "replaced" }) : state.TerminalResult);
        commit = commit with { ModelState = new(state.Revision, changed, commit.ModelState.Input) };
        await Assert.ThrowsAsync<InvalidOperationException>(() => Store(context).CommitAsync(commit, Immediate).AsTask());
        Assert.Null(await Reader(context).FindInputAsync("execution", "obsolete"));
        Assert.Single(await context.RuntimeCheckpointCommits.ToArrayAsync());
    }

    [Fact]
    public async Task Retained_input_still_compares_the_physical_revision_at_commit()
    {
        await using var database = await CreateDatabaseAsync();
        await using (var context = database.Open()) await Store(context).CommitAsync(Commit(1), Immediate);
        var advance = new AdvanceBeforeModelCompare();
        await using (var context = database.Open(advance))
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => Store(context).CommitAsync(Retained(1), Immediate).AsTask());
        Assert.True(advance.Changed);
        await using var restarted = database.Open();
        Assert.Equal(1, (await Reader(restarted).FindAsync("execution"))!.Revision);
        Assert.Null(await Reader(restarted).FindInputAsync("execution", "obsolete"));
        Assert.Single(await restarted.RuntimeCheckpointCommits.ToArrayAsync());
    }

    [Fact]
    public async Task Retained_input_marker_failure_rolls_back_receipt_and_the_fence_touch()
    {
        await using var database = await CreateDatabaseAsync();
        long revision;
        await using (var context = database.Open())
        {
            await Store(context).CommitAsync(Commit(1), Immediate);
            revision = (await context.ExecutionLivenessStates.SingleAsync()).Revision;
        }
        await using (var context = database.Open(new FailMarker()))
            await Assert.ThrowsAsync<DbUpdateException>(() => Store(context).CommitAsync(Retained(1), Immediate).AsTask());
        await using var restarted = database.Open();
        Assert.Null(await Reader(restarted).FindInputAsync("execution", "obsolete"));
        Assert.Equal(revision, (await restarted.ExecutionLivenessStates.SingleAsync()).Revision);
        Assert.Single(await restarted.RuntimeCheckpointCommits.ToArrayAsync());
    }

    private RuntimeCheckpointCommit Retained(long revision, RuntimeModelLifecycle lifecycle = RuntimeModelLifecycle.Running)
    {
        var state = Change(revision, lifecycle: lifecycle).State;
        var at = Now.AddSeconds(5);
        var input = new RuntimeModelInputReceipt(state.Scope, state.ExecutionId, "obsolete", "test.input", "obsolete-content", revision, at,
            "invocation", "obsolete-attempt", JsonSerializer.SerializeToElement(new { status = "Rejected" }));
        return new("obsolete", new("obsolete", "ModelDecision", "execution", at, [], new Dictionary<string, string>()),
            new(null, null, [], [], [], [], []), [], new Dictionary<string, string>())
            { ExpectedFence = Lease.ToFence(), ModelState = new(revision, state, input) };
    }

    private sealed class AdvanceBeforeModelCompare : DbCommandInterceptor
    {
        public bool Changed { get; private set; }
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (!Changed && command.CommandText.StartsWith("UPDATE", StringComparison.Ordinal) && command.CommandText.Contains("\"elsa_runtime_model_execution\"", StringComparison.Ordinal))
            {
                Changed = true;
                await using var advance = command.Connection!.CreateCommand();
                advance.Transaction = command.Transaction;
                advance.CommandText = "UPDATE elsa_runtime_model_execution SET Revision = Revision + 1";
                await advance.ExecuteNonQueryAsync(cancellationToken);
            }
            return result;
        }
    }

    [Fact]
    public async Task Decisions_survive_context_restart_with_exact_receipts_and_no_native_header()
    {
        await using var database = await CreateDatabaseAsync();
        var first = Commit(1);
        await using (var context = database.Open())
        {
            var store = Store(context);
            await store.CommitAsync(first, Immediate);
            await store.CommitAsync(Commit(2), Immediate);
            await store.CommitAsync(first, Immediate); // Replay after state advanced must not overwrite it.
            await Assert.ThrowsAsync<RuntimeCheckpointReplayConflictException>(() =>
                store.CommitAsync(first with { ModelState = Change(1, payload: "{\"tokens\":[\"conflicting\"]}") }, Immediate).AsTask());
        }
        await using (var restarted = database.Open())
        {
            var reader = Reader(restarted);
            var state = Assert.IsType<RuntimeModelExecutionState>(await reader.FindAsync("execution"));
            Assert.Equal(2, state.Revision);
            Assert.Equal("human-wait", state.Payload.GetProperty("tokens")[0].GetString());
            Assert.Equal(Pin, state.Executable);
            Assert.Equal(materialIncarnation, state.MaterialIncarnationId);
            Assert.Equal(1, (await reader.FindInputAsync("execution", "input-1"))!.AcceptedRevision);
            Assert.Equal(2, (await reader.FindInputAsync("execution", "input-2"))!.AcceptedRevision);
            Assert.Empty(await restarted.WorkflowExecutionStates.ToArrayAsync());
            Assert.Empty(await restarted.ActivityExecutionStates.ToArrayAsync());
            Assert.Equal(2, await restarted.RuntimeCheckpointCommits.CountAsync());
            await Store(restarted).CommitAsync(Commit(3, lifecycle: RuntimeModelLifecycle.TerminalSettled), Immediate);
        }
        await using var completed = database.Open();
        Assert.Equal(RuntimeModelLifecycle.TerminalSettled, (await Reader(completed).FindAsync("execution"))!.Lifecycle);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Store(completed).CommitAsync(Commit(4), Immediate).AsTask());
        Assert.Null(await Reader(completed).FindInputAsync("execution", "input-4"));
    }

    [Fact]
    public async Task Failure_at_the_final_marker_rolls_back_state_receipt_and_fence_touch()
    {
        await using var database = await CreateDatabaseAsync();
        long fenceRevision;
        await using (var before = database.Open()) fenceRevision = (await before.ExecutionLivenessStates.SingleAsync()).Revision;
        await using (var context = database.Open(new FailMarker()))
        {
            var failure = await Assert.ThrowsAsync<DbUpdateException>(() => Store(context).CommitAsync(Commit(1), Immediate).AsTask());
            Assert.Equal("Injected final-marker failure.", failure.InnerException!.Message);
        }
        await using var restarted = database.Open();
        Assert.Empty(await restarted.Set<RuntimeModelExecutionEntity>().ToArrayAsync());
        Assert.Empty(await restarted.Set<RuntimeModelInputEntity>().ToArrayAsync());
        Assert.Empty(await restarted.RuntimeCheckpointCommits.ToArrayAsync());
        Assert.Equal(fenceRevision, (await restarted.ExecutionLivenessStates.SingleAsync()).Revision);
        await Store(restarted).CommitAsync(Commit(1), Immediate);
        Assert.NotNull(await Reader(restarted).FindInputAsync("execution", "input-1"));
    }

    [Fact]
    public async Task Stale_fence_revision_and_reused_input_refuse_without_partial_decisions()
    {
        await using var database = await CreateDatabaseAsync();
        await using var context = database.Open();
        var store = Store(context);
        await store.CommitAsync(Commit(1), Immediate);
        await Assert.ThrowsAsync<RuntimeStaleFencingTokenException>(() => store.CommitAsync(Commit(2) with
            { ExpectedFence = new("obsolete", "old-owner", 1) }, Immediate).AsTask());
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => store.CommitAsync(Commit(3), Immediate).AsTask());
        var duplicate = Commit(2) with { ModelState = Change(2, inputId: "input-1") };
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.CommitAsync(duplicate, Immediate).AsTask());
        Assert.Equal(1, (await Reader(context).FindAsync("execution"))!.Revision);
        Assert.Single(await context.RuntimeCheckpointCommits.ToArrayAsync());
        Assert.Null(await Reader(context).FindInputAsync("execution", "input-2"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Model_state_input_consumed_claim_outbox_and_marker_share_one_transaction(bool failMarker)
    {
        await using var database = await CreateDatabaseAsync();
        await using (var context = failMarker ? database.Open(new FailMarker()) : database.Open())
        {
            var queue = new EfSchedulerWorkQueueStore(context, new Access("tenant-a"), new Codec());
            await queue.EnqueueAsync(new("model-input", "execution", "command", WorkflowExecutionCommandKind.ScheduleActivity,
                "envelope", "enqueue-input", Now, Now, 1));
            var claim = (await queue.ClaimAsync(new("execution", "queue-owner", Now, TimeSpan.FromMinutes(5))))!;
            var commit = Commit(1);
            var intent = new RuntimePostCommitIntent("next", "execution", "test.intent", Now.AddSeconds(1), null, null, null);
            var outboxId = RuntimePostCommitOutboxIdentity.CreateLogicalValue(commit.CommitId, intent.IntentId);
            var item = new RuntimePostCommitOutboxItem(outboxId, intent, RuntimePostCommitOutboxStatus.Pending, Now.AddSeconds(1), Now.AddSeconds(1));
            commit = commit with
            {
                PostCommitIntents = [intent],
                StateChanges = commit.StateChanges.WithConsumedSchedulerWorkItems([ConsumedSchedulerWorkItem.FromClaim(claim)])
                    .WithPostCommitOutbox([new(outboxId, RuntimeStateChangeOperation.Upsert, item, new Dictionary<string, string>())])
            };
            if (failMarker)
                await Assert.ThrowsAsync<DbUpdateException>(() => Store(context).CommitAsync(commit, Immediate).AsTask());
            else
            {
                var accepted = await Store(context).CommitAsync(commit, Immediate);
                Assert.Equal(["model-input"], accepted.ConsumedSchedulerWorkItemIds);
                Assert.Equal([outboxId], accepted.PendingPostCommitWorkIds);
            }
        }
        await using var reopened = database.Open();
        Assert.Equal(failMarker ? 1 : 0, await reopened.SchedulerWorkItems.CountAsync());
        var expected = failMarker ? 0 : 1;
        Assert.Equal(expected, await reopened.Set<RuntimeModelExecutionEntity>().CountAsync());
        Assert.Equal(expected, await reopened.Set<RuntimeModelInputEntity>().CountAsync());
        Assert.Equal(expected, await reopened.RuntimePostCommitOutbox.CountAsync());
        Assert.Equal(expected, await reopened.RuntimeCheckpointCommits.CountAsync());
    }

    [Theory]
    [InlineData("incarnation")]
    [InlineData("authority")]
    [InlineData("pin")]
    [InlineData("protocol")]
    public async Task An_update_cannot_replace_the_execution_pin_or_authority(string field)
    {
        await using var database = await CreateDatabaseAsync();
        await using var context = database.Open();
        await Store(context).CommitAsync(Commit(1), Immediate);
        var change = Change(2, incarnation: field == "incarnation" ? "replacement" : null,
            authority: field == "authority" ? new("foreign", "foreign") : null,
            pin: field == "pin" ? Pin with { DefinitionVersionId = "different-version" } : null,
            protocol: field == "protocol" ? new("different.model", 1) : null);
        await Assert.ThrowsAsync<InvalidOperationException>(() => Store(context).CommitAsync(Commit(2) with { ModelState = change }, Immediate).AsTask());
        Assert.Equal(1, (await Reader(context).FindAsync("execution"))!.Revision);
        Assert.Single(await context.Set<RuntimeModelInputEntity>().ToArrayAsync());
    }

    [Theory]
    [InlineData(RuntimeCheckpointPersistenceMode.Deferred)]
    [InlineData(RuntimeCheckpointPersistenceMode.Skip)]
    public async Task A_model_decision_cannot_be_buffered_or_skipped(RuntimeCheckpointPersistenceMode mode)
    {
        await using var database = await CreateDatabaseAsync();
        await using var context = database.Open();
        await Assert.ThrowsAsync<NotSupportedException>(() => Store(context).CommitAsync(Commit(1), new(mode)).AsTask());
        Assert.Empty(await context.RuntimeCheckpointCommits.ToArrayAsync());
    }

    [Fact]
    public async Task Material_incarnation_and_all_five_pin_fields_are_verified_before_acceptance()
    {
        await using var database = await CreateDatabaseAsync();
        await using var context = database.Open();
        await Assert.ThrowsAsync<InvalidDataException>(() => Store(context).CommitAsync(Commit(1) with
            { ModelState = Change(1, incarnation: "replacement") }, Immediate).AsTask());
        await Assert.ThrowsAsync<InvalidDataException>(() => Store(context).CommitAsync(Commit(1) with
            { ModelState = Change(1, pin: Pin with { DefinitionVersionId = "other" }) }, Immediate).AsTask());
        Assert.Empty(await context.Set<RuntimeModelExecutionEntity>().ToArrayAsync());
        Assert.Empty(await context.RuntimeCheckpointCommits.ToArrayAsync());
    }

    [Fact]
    public async Task Scoped_reader_pages_and_rejects_corrupt_projection_or_foreign_continuation()
    {
        await using var database = await CreateDatabaseAsync();
        await using var context = database.Open();
        await Store(context).CommitAsync(Commit(1), Immediate);
        var reader = Reader(context);
        Assert.Single((await reader.ListPageAsync(new(1))).Items);
        Assert.Null(await Reader(context, "foreign").FindAsync("execution"));
        var forged = new Codec().Encode("runtime.model-executions.v1", JsonSerializer.SerializeToUtf8Bytes(new
            { Version = 1, ScopeHash = Hash("foreign"), ExecutionId = "execution", After = Order("execution") }));
        await Assert.ThrowsAsync<ArgumentException>(() => reader.ListPageAsync(new(1, forged)).AsTask());
        await context.Set<RuntimeModelExecutionEntity>().ExecuteUpdateAsync(x => x.SetProperty(row => row.Lifecycle, 999));
        await Assert.ThrowsAsync<InvalidDataException>(() => reader.FindAsync("execution").AsTask());
    }

    [Fact]
    public async Task Capability_qualification_requires_the_actual_scoped_reader_context_and_access()
    {
        await using var database = await CreateDatabaseAsync();
        await using var context = database.Open();
        await using var other = database.Open();
        var access = new Access("tenant-a");
        IRuntimeCheckpointCommitStore writer = new EfRuntimeCheckpointCommitStore(context, access, new Clock(), new LeaseManager());
        Assert.True(writer.SupportsModelCheckpoint);
        Assert.True(writer.CanCommitModelState(new EfRuntimeModelExecutionReader(context, access, new Codec())));
        Assert.False(writer.CanCommitModelState(new EfRuntimeModelExecutionReader(other, access, new Codec())));
        Assert.False(writer.CanCommitModelState(new EfRuntimeModelExecutionReader(context, new Access("tenant-a"), new Codec())));
    }

    [Fact]
    public async Task A_real_model_checkpoint_holds_actual_artifact_grants_and_refuses_a_deletion_guard()
    {
        await using var database = await CreateDatabaseAsync();
        await using var context = database.Open();
        var access = new Access("tenant-a");
        IWorkflowExecutableStore artifacts = new EfWorkflowExecutableStore(context, access);
        var leases = new WorkflowExecutableRootWriteLeaseManager(artifacts, Options.Create(new WorkflowExecutableGarbageCollectionOptions()), new Clock());
        var store = new EfRuntimeCheckpointCommitStore(context, access, new Clock(), leases);
        var guard = (await artifacts.TryBeginDeletionAsync(Pin.ArtifactId, "deletion", Now.AddMinutes(1), Now))!;
        await Assert.ThrowsAsync<WorkflowExecutableRootWriteLeaseUnavailableException>(() => store.CommitAsync(Commit(1), Immediate).AsTask());
        Assert.Null(await Reader(context).FindAsync("execution"));
        Assert.True(await artifacts.CancelDeletionAsync(guard));
        await store.CommitAsync(Commit(1), Immediate);
        Assert.Equal(materialIncarnation, (await Reader(context).FindAsync("execution"))!.MaterialIncarnationId);
        Assert.Empty(await context.WorkflowExecutableRootWriteLeases.ToArrayAsync());
    }

    [Fact]
    public async Task A_fenced_model_publication_and_retained_execution_protect_the_actual_model_material()
    {
        await using var database = await CreateDatabaseAsync();
        await using var context = database.Open();
        var access = new Access("tenant-a");
        IWorkflowExecutableStore artifacts = new EfWorkflowExecutableStore(context, access);
        var sources = new EfWorkflowExecutableSourceReferenceStore(context, access, new Codec(), new Clock());
        var leases = new WorkflowExecutableRootWriteLeaseManager(artifacts, Options.Create(new WorkflowExecutableGarbageCollectionOptions()), new Clock());
        var reference = new WorkflowExecutableSourceReference("model-published", Pin.ArtifactId, "test.model", "process", "1",
            Pin.DefinitionId, Pin.DefinitionVersionId, Pin.ArtifactVersion, Now, Now, WorkflowExecutableReferenceScope.Published, TenantId: "tenant-a");
        await leases.ExecuteFencedAsync([Pin], "publish-model", async (grants, token) =>
            await sources.SaveWithRootWriteLeasesAsync(reference, Pin, grants, token));
        Assert.NotNull(await sources.FindAsync(reference.SourceReferenceId));
        Assert.Null(await artifacts.FindAsync(Pin.ArtifactId)); // Model material never manufactures a native root.
        Assert.Empty((await artifacts.ListPageAsync(new(10))).Items);
        Assert.NotNull(Assert.Single((await artifacts.ListMaterialPageAsync(new(10))).Items).Model);
        await new EfRuntimeCheckpointCommitStore(context, access, new Clock(), leases).CommitAsync(Commit(1), Immediate);
        Assert.True(await sources.RetireAsync(reference.SourceReferenceId, Now.AddMinutes(1), "retired"));
        var executions = new EfWorkflowExecutionStateStore(context, access, new Codec());
        Assert.Contains(Pin.ArtifactId, await executions.ListPinnedExecutableArtifactIdsAsync());
        Assert.Empty(await executions.ListAsync());
        var collector = new WorkflowExecutableReferenceGarbageCollector(artifacts, sources, new InMemoryExecutableActivityTemplateStore(),
            executions, null, Options.Create(new WorkflowExecutableGarbageCollectionOptions { ArtifactCreationGracePeriod = TimeSpan.Zero }),
            new Clock(), NullLogger<WorkflowExecutableReferenceGarbageCollector>.Instance);
        var result = await collector.SweepAsync(Now.AddDays(2));
        Assert.Equal(0, result.DeletedArtifactCount);
        Assert.Equal(0, result.DeletedReferenceCount);
        Assert.Equal(materialIncarnation, (await artifacts.FindMaterialAsync(Pin.ArtifactId))!.IncarnationId);
        Assert.NotNull(await sources.FindAsync(reference.SourceReferenceId));
    }

    [Fact]
    public async Task Unreferenced_model_material_is_collected_after_its_actual_staging_grace()
    {
        await using var database = await CreateDatabaseAsync();
        await using var context = database.Open();
        var access = new Access("tenant-a");
        IWorkflowExecutableStore artifacts = new EfWorkflowExecutableStore(context, access);
        var collector = new WorkflowExecutableReferenceGarbageCollector(artifacts,
            new EfWorkflowExecutableSourceReferenceStore(context, access, new Codec()), new InMemoryExecutableActivityTemplateStore(),
            new EfWorkflowExecutionStateStore(context, access, new Codec()), null,
            Options.Create(new WorkflowExecutableGarbageCollectionOptions { ArtifactCreationGracePeriod = TimeSpan.FromMinutes(1) }),
            new Clock(), NullLogger<WorkflowExecutableReferenceGarbageCollector>.Instance);
        var createdAt = (await artifacts.FindMaterialAsync(Pin.ArtifactId))!.CreatedAt;
        Assert.Equal(0, (await collector.SweepAsync(createdAt)).DeletedArtifactCount);
        Assert.Equal(1, (await collector.SweepAsync(createdAt.AddMinutes(2))).DeletedArtifactCount);
        Assert.Null(await artifacts.FindMaterialAsync(Pin.ArtifactId));
    }

    [Fact]
    public async Task Model_dependency_pins_receive_real_closure_grants_and_refuse_foreign_source_identity()
    {
        await using var database = await CreateDatabaseAsync();
        await using var context = database.Open();
        IWorkflowExecutableStore artifacts = new CachingWorkflowExecutableStore(new EfWorkflowExecutableStore(context, new Access("tenant-a")), new WorkflowExecutableCacheOptions());
        var child = (await artifacts.FindMaterialAsync(Pin.ArtifactId))!;
        var correct = Parent(child.Identity, "parent-good");
        await artifacts.SaveModelAsync(correct);
        var leases = new WorkflowExecutableRootWriteLeaseManager(artifacts, Options.Create(new WorkflowExecutableGarbageCollectionOptions()), new Clock());
        await leases.ExecuteFencedAsync([correct.Identity], "dependency-proof", (grants, token) =>
        {
            Assert.Equal(new[] { Pin.ArtifactId, correct.Identity.ArtifactId }.Order(StringComparer.Ordinal),
                grants.Select(x => x.ArtifactId).Order(StringComparer.Ordinal));
            return ValueTask.CompletedTask;
        });
        var wrong = Parent(child.Identity with { DefinitionVersionId = "foreign-version" }, "parent-wrong");
        await artifacts.SaveModelAsync(wrong);
        var called = false;
        await Assert.ThrowsAsync<WorkflowExecutableDependencyGraphException>(() => leases.ExecuteAsync(wrong.Identity, "foreign-child", token =>
        { called = true; return ValueTask.CompletedTask; }).AsTask());
        Assert.False(called);
        Assert.Empty(await context.WorkflowExecutableRootWriteLeases.ToArrayAsync());

        RuntimeModelMaterial Parent(WorkflowExecutableIdentity dependency, string id)
        {
            using var definition = JsonDocument.Parse("{\"start\":\"dependent\"}");
            var seed = new WorkflowExecutableIdentity(id, "parent", "parent-v1", "1.0.0", "pending");
            var material = new RuntimeModelMaterial(1, seed, Protocol, "test/1", definition.RootElement, new(1, []), [], [new("child", dependency)], []);
            return new(1, seed with { ArtifactHash = material.ComputeContentHash() }, Protocol, "test/1", definition.RootElement, new(1, []), [], [new("child", dependency)], []);
        }
    }

    [Fact]
    public async Task Model_parent_admission_preserves_the_real_wait_and_replays_after_parent_cancellation()
    {
        await using var database = await CreateDatabaseAsync();
        var expected = await PrepareNativeParentAsync(database);
        var commit = Commit(1) with { ExpectedModelParentWait = expected };
        await using (var context = database.Open()) await Store(context).CommitAsync(commit, Immediate);
        await using var restarted = database.Open();
        var bookmarks = new EfBookmarkStateStore(restarted, new Access("tenant-a"));
        Assert.Equal(expected.RegistrationCapture!.Generation, (await bookmarks.ObserveAsync("parent", "wait"))!.Generation);
        Assert.Equal(ActivityExecutionStatus.Suspended, (await new EfActivityExecutionStateStore(restarted, new Access("tenant-a"), new Codec()).FindAsync("parent", "activity"))!.Status);
        var executions = new EfWorkflowExecutionStateStore(restarted, new Access("tenant-a"), new Codec());
        await executions.SaveAsync((await executions.FindAsync("parent"))! with { Status = WorkflowExecutionStatus.Cancelled });
        await Store(restarted).CommitAsync(commit, Immediate);
        Assert.Equal(1, (await Reader(restarted).FindAsync("execution"))!.Revision);
        Assert.Single(await restarted.RuntimeCheckpointCommits.ToArrayAsync());
    }

    [Theory]
    [InlineData("parent-cancelled")]
    [InlineData("activity-cancelled")]
    [InlineData("rearmed")]
    public async Task A_changed_native_parent_cannot_admit_its_model_child(string change)
    {
        await using var database = await CreateDatabaseAsync();
        var expected = await PrepareNativeParentAsync(database);
        await using var context = database.Open();
        var access = new Access("tenant-a");
        if (change == "parent-cancelled")
        {
            var executions = new EfWorkflowExecutionStateStore(context, access, new Codec());
            await executions.SaveAsync((await executions.FindAsync("parent"))! with { Status = WorkflowExecutionStatus.Cancelled });
        }
        if (change == "activity-cancelled")
        {
            var activities = new EfActivityExecutionStateStore(context, access, new Codec());
            await activities.SaveAsync((await activities.FindAsync("parent", "activity"))! with { Status = ActivityExecutionStatus.Cancelled });
        }
        if (change == "rearmed")
        {
            var bookmarks = new EfBookmarkStateStore(context, access);
            await bookmarks.SaveAsync((await bookmarks.FindAsync("parent", "wait"))!);
        }
        await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() => Store(context).CommitAsync(Commit(1) with { ExpectedModelParentWait = expected }, Immediate).AsTask());
        await using var restarted = database.Open();
        Assert.Null(await Reader(restarted).FindAsync("execution"));
        Assert.Empty(await restarted.RuntimeCheckpointCommits.ToArrayAsync());
    }

    [Theory]
    [InlineData("elsa_runtime_bookmark_state")]
    [InlineData("elsa_runtime_workflow_execution_state")]
    [InlineData("elsa_runtime_activity_execution_state")]
    public async Task Model_parent_admission_physically_compares_each_live_native_participant(string table)
    {
        await using var database = await CreateDatabaseAsync();
        var expected = await PrepareNativeParentAsync(database);
        var advance = new AdvanceBeforeParentCompare(table);
        await using (var context = database.Open(advance))
            await Assert.ThrowsAsync<RuntimeSchedulerWorkUnavailableException>(() => Store(context).CommitAsync(Commit(1) with { ExpectedModelParentWait = expected }, Immediate).AsTask());
        Assert.True(advance.Changed);
        await using var restarted = database.Open();
        Assert.Null(await Reader(restarted).FindAsync("execution"));
        Assert.Empty(await restarted.RuntimeCheckpointCommits.ToArrayAsync());
    }

    private async Task<StimulusResumeRecipient> PrepareNativeParentAsync(Database database)
    {
        await using var context = database.Open();
        var access = new Access("tenant-a");
        var parent = new WorkflowExecutionState("parent", new("native-parent", "parent", "parent-v1", "1.0.0", "parent-hash"),
            WorkflowExecutionStatus.Running, null, Now, Now, Now, null, null, null, "tenant-a", new Dictionary<string, string>())
            { Partition = new("tenant-a"), Authority = Change(1).State.Authority, RunKind = WorkflowRunKind.PublishedRun };
        await new EfWorkflowExecutionStateStore(context, access, new Codec()).SaveAsync(parent);
        var activity = new ActivityExecutionState(new("activity", "parent", "node", "node", "Test.RunModel", "1"),
            ActivityExecutionStatus.Suspended, null, 1, Now, Now, null, null, null, null, null,
            ActivitySchedulingProvenance.From("parent", null, null, null, null, null, null, "fixture"), null, ["wait"], [], 0, 0, new Dictionary<string, string>());
        await new EfActivityExecutionStateStore(context, access, new Codec()).SaveAsync(activity);
        var bookmarks = new EfBookmarkStateStore(context, access);
        await bookmarks.SaveAsync(new("wait", "parent", "activity", "node", "resume", "test-model-result", "execution", null,
            new Dictionary<string, string>(), Now, Now.AddMinutes(5)));
        return StimulusResumeRecipient.From((await bookmarks.ObserveAsync("parent", "wait"))!, parent, new("tenant-a"));
    }

    private sealed class AdvanceBeforeParentCompare(string table) : DbCommandInterceptor
    {
        public bool Changed { get; private set; }
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (!Changed && command.CommandText.StartsWith("UPDATE", StringComparison.Ordinal) && command.CommandText.Contains('"' + table + '"', StringComparison.Ordinal))
            {
                Changed = true;
                await using var advance = command.Connection!.CreateCommand(); advance.Transaction = command.Transaction;
                advance.CommandText = "UPDATE \"" + table + "\" SET Revision = Revision + 1";
                await advance.ExecuteNonQueryAsync(cancellationToken);
            }
            return result;
        }
    }

    private RuntimeCheckpointCommit Commit(long revision, RuntimeModelLifecycle lifecycle = RuntimeModelLifecycle.Running) =>
        new($"commit-{revision}", new($"checkpoint-{revision}", "ModelDecision", "execution", Now.AddSeconds(revision), [], new Dictionary<string, string>()),
            new(null, null, [], [], [], [], []), [], new Dictionary<string, string>())
        { ExpectedFence = Lease.ToFence(), ModelState = Change(revision, lifecycle: lifecycle) };

    private RuntimeModelStateChange Change(long revision, string? inputId = null, string? payload = null,
        string? incarnation = null, WorkflowExecutionAuthoritySnapshot? authority = null, WorkflowExecutableIdentity? pin = null,
        RuntimeExecutionProtocol? protocol = null, RuntimeModelLifecycle lifecycle = RuntimeModelLifecycle.Running)
    {
        using var data = JsonDocument.Parse(payload ?? "{\"tokens\":[\"human-wait\"]}");
        using var result = JsonDocument.Parse("{\"outcome\":\"completed\"}");
        var at = Now.AddSeconds(revision);
        var state = new RuntimeModelExecutionState(1, new("tenant-a"), "execution", "tenant-a", protocol ?? Protocol,
            pin ?? Pin, incarnation ?? materialIncarnation, revision, lifecycle, authority ?? new("system", "initiator"), Now, at,
            data.RootElement, lifecycle is RuntimeModelLifecycle.TerminalSettled or RuntimeModelLifecycle.TerminalPendingSettlement ? result.RootElement : null);
        return new(revision == 1 ? null : revision - 1, state,
            new(new("tenant-a"), "execution", inputId ?? $"input-{revision}", "test.input", $"content-{revision}", revision, at, null, null, result.RootElement));
    }

    private async Task<Database> CreateDatabaseAsync()
    {
        var database = await Database.CreateAsync();
        materialIncarnation = database.Incarnation;
        return database;
    }

    private static WorkflowExecutableIdentity CreatePin()
    {
        using var definition = JsonDocument.Parse("{\"start\":\"human\"}");
        var seed = new WorkflowExecutableIdentity("model-artifact", "process", "process-v1", "1.0.0", "pending");
        var material = new RuntimeModelMaterial(1, seed, Protocol, "test/1", definition.RootElement, new(1, []), [], [], []);
        return seed with { ArtifactHash = material.ComputeContentHash() };
    }

    private static EfRuntimeCheckpointCommitStore Store(RuntimeDbContext context) => new(context, new Access("tenant-a"), new Clock(), new LeaseManager());
    private static EfRuntimeModelExecutionReader Reader(RuntimeDbContext context, string scope = "tenant-a") => new(context, new Access(scope), new Codec());
    private sealed class Access(string scope) : IPersistenceAccessContextAccessor
    { public PersistenceAccessContext Current { get; } = PersistenceAccessContext.Scoped(new(scope)); }
    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class LeaseManager : IWorkflowExecutableRootWriteLeaseManager
    {
        public ValueTask ExecuteAsync(string artifactId, string leaseId, Func<CancellationToken, ValueTask> write, CancellationToken cancellationToken = default) => write(cancellationToken);
    }
    private sealed class Codec : IRuntimeRecoveryContinuationCodec
    {
        public string Encode(string purpose, ReadOnlySpan<byte> payload) => purpose + ":" + Convert.ToBase64String(payload);
        public byte[] Decode(string purpose, string token) => token.StartsWith(purpose + ":", StringComparison.Ordinal)
            ? Convert.FromBase64String(token[(purpose.Length + 1)..]) : throw new FormatException();
    }
    private sealed class FailMarker : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData,
            InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("INSERT INTO \"elsa_runtime_checkpoint_commit\"", StringComparison.Ordinal))
                throw new InvalidOperationException("Injected final-marker failure.");
            return ValueTask.FromResult(result);
        }
    }
    private sealed class Database(SqliteConnection connection) : IAsyncDisposable
    {
        public string Incarnation { get; private set; } = null!;
        public static async Task<Database> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
            var database = new Database(connection);
            await using var context = database.Open(); await context.Database.EnsureCreatedAsync();
            await new EfExecutionLivenessStateStore(context, new Access("tenant-a"), new Codec())
                .SaveAsync(new("ownership:execution", "execution", Lease, null, null, null));
            using var definition = JsonDocument.Parse("{\"start\":\"human\"}");
            var material = new RuntimeModelMaterial(1, Pin, Protocol, "test/1", definition.RootElement, new(1, []), [], [], []);
            IWorkflowExecutableStore artifacts = new EfWorkflowExecutableStore(context, new Access("tenant-a"));
            await artifacts.SaveModelAsync(material);
            database.Incarnation = (await artifacts.FindMaterialAsync(Pin.ArtifactId))!.IncarnationId;
            return database;
        }
        public RuntimeSqliteDbContext Open(params IInterceptor[] interceptors)
        {
            var options = new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(connection);
            if (interceptors.Length > 0) options.AddInterceptors(interceptors);
            return new(options.Options);
        }
        public ValueTask DisposeAsync() => connection.DisposeAsync();
    }
}
