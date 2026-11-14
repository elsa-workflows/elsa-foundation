using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.Schema;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class EfSchedulerWorkQueueStoreTests
{

    [Fact]
    public async Task Process_private_lexical_header_cannot_be_serialized_for_shared_storage()
    {
        await using var database = await TestDatabase.CreateAsync(); await using var fixture = database.Open("tenant-a");
        var state = TestExecution(DateTimeOffset.UtcNow);
        state = state with { TestContext = new(state.TestEntry!.ContextId, state.TestEntry.ContextFingerprint, [], [], [state.TestEntry.ExecutableNodeId]) };
        Assert.Throws<NotSupportedException>(() => EfWorkflowExecutionStateStore.ToEntity(state, "tenant-a",
            EfWorkflowExecutionStateStore.CreateId("tenant-a", state.WorkflowExecutionId), 1));
        Assert.Empty(await fixture.Context.WorkflowExecutionStates.AsNoTracking().ToArrayAsync());
    }

    [Fact]
    public async Task Process_private_lexical_header_is_refused_by_shared_reader_even_when_physically_injected()
    {
        await using var database = await TestDatabase.CreateAsync(); await using var fixture = database.Open("tenant-a");
        var state = TestExecution(DateTimeOffset.UtcNow);
        fixture.Context.WorkflowExecutionStates.Add(EfWorkflowExecutionStateStore.ToEntity(state, "tenant-a",
            EfWorkflowExecutionStateStore.CreateId("tenant-a", state.WorkflowExecutionId), 1));
        await fixture.Context.SaveChangesAsync();
        var captured = state with { TestContext = new(state.TestEntry!.ContextId, state.TestEntry.ContextFingerprint,
            [], [], [state.TestEntry.ExecutableNodeId]) };
        var json = JsonSerializer.Serialize(captured);
        await fixture.Context.WorkflowExecutionStates.ExecuteUpdateAsync(update => update.SetProperty(row => row.ContentJson, json));
        fixture.Context.ChangeTracker.Clear();
        var row = await fixture.Context.WorkflowExecutionStates.AsNoTracking().SingleAsync();
        Assert.Throws<NotSupportedException>(() => EfWorkflowExecutionStateStore.ReadChecked(row, "tenant-a", state.WorkflowExecutionId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Process_private_lexical_header_is_refused_by_physical_save_guard(bool synchronous)
    {
        await using var database = await TestDatabase.CreateAsync(); await using var fixture = database.Open("tenant-a");
        var state = TestExecution(DateTimeOffset.UtcNow);
        var row = EfWorkflowExecutionStateStore.ToEntity(state, "tenant-a", EfWorkflowExecutionStateStore.CreateId("tenant-a", state.WorkflowExecutionId), 1);
        var json = System.Text.Json.Nodes.JsonNode.Parse(row.ContentJson)!;
        json["TestContext"] = new System.Text.Json.Nodes.JsonObject { ["ContextId"] = "private" };
        row.ContentJson = json.ToJsonString(); fixture.Context.WorkflowExecutionStates.Add(row);
        if (synchronous) Assert.Throws<NotSupportedException>(() => fixture.Context.SaveChanges());
        else await Assert.ThrowsAsync<NotSupportedException>(() => fixture.Context.SaveChangesAsync());
        Assert.Empty(await fixture.Context.WorkflowExecutionStates.AsNoTracking().ToArrayAsync());
    }

    [Theory]
    [InlineData(WorkflowExecutionCommandKind.Start, false)]
    [InlineData(WorkflowExecutionCommandKind.Checkpoint, false)]
    [InlineData(WorkflowExecutionCommandKind.Start, true)]
    [InlineData(WorkflowExecutionCommandKind.Checkpoint, true)]
    public async Task Process_private_selected_interpreter_is_refused_before_shared_queue_writes(WorkflowExecutionCommandKind kind, bool existingRoot)
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        var now = DateTimeOffset.UtcNow; var state = TestExecution(now);
        if (existingRoot)
        {
            fixture.Context.WorkflowExecutionStates.Add(EfWorkflowExecutionStateStore.ToEntity(state, "tenant-a",
                EfWorkflowExecutionStateStore.CreateId("tenant-a", state.WorkflowExecutionId), 1));
            await fixture.Context.SaveChangesAsync();
        }
        var payload = kind == WorkflowExecutionCommandKind.Start
            ? JsonSerializer.SerializeToElement(new WorkflowExecutionStartCommandPayload(state.PinnedExecutable, state.PinnedExecutable.ArtifactId) { TestEntry = state.TestEntry })
            : JsonSerializer.SerializeToElement(new RuntimeCheckpointCommandPayload(state.PinnedExecutable, "WorkflowStarted", [], "WorkflowStart") { TestEntry = state.TestEntry });
        await Assert.ThrowsAsync<NotSupportedException>(async () => await fixture.Store.EnqueueAsync(new("private", state.WorkflowExecutionId,
            "command", kind, "envelope", "key", now, now, payload: payload)));
        Assert.Empty(await fixture.Context.SchedulerWorkItems.AsNoTracking().ToArrayAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Process_private_selected_interpreter_is_refused_before_shared_outbox_writes(bool existingRoot)
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        var now = DateTimeOffset.UtcNow; var state = TestExecution(now);
        if (existingRoot)
        {
            fixture.Context.WorkflowExecutionStates.Add(EfWorkflowExecutionStateStore.ToEntity(state, "tenant-a",
                EfWorkflowExecutionStateStore.CreateId("tenant-a", state.WorkflowExecutionId), 1));
            await fixture.Context.SaveChangesAsync();
        }
        var payload = JsonSerializer.SerializeToElement(new WorkflowExecutionStartCommandPayload(state.PinnedExecutable,
            state.PinnedExecutable.ArtifactId) { TestEntry = state.TestEntry });
        var work = new RuntimeSchedulerWorkItem("private", state.WorkflowExecutionId, "command", WorkflowExecutionCommandKind.Start,
            "envelope", "key", now, now, payload: payload);
        var item = new RuntimePostCommitOutboxItem("outbox", new("intent", state.WorkflowExecutionId,
            Elsa.Workflows.Runtime.Core.Constants.RuntimePostCommitIntentKinds.EnqueueSchedulerWork, now, null, "key",
            JsonSerializer.SerializeToElement(work)), RuntimePostCommitOutboxStatus.Pending, now, now);
        await Assert.ThrowsAsync<NotSupportedException>(async () =>
            await new EfRuntimePostCommitOutboxStore(fixture.Context, new FixedAccessor("tenant-a")).SavePendingAsync(item));
        Assert.Empty(await fixture.Context.RuntimePostCommitOutbox.AsNoTracking().ToArrayAsync());
    }

    [Theory]
    [InlineData(WorkflowExecutionCommandKind.Start)]
    [InlineData(WorkflowExecutionCommandKind.Checkpoint)]
    public async Task Test_entry_admission_requires_a_predecessor_unreadable_queue_format(WorkflowExecutionCommandKind kind)
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        var now = DateTimeOffset.UtcNow;
        var payload = JsonSerializer.SerializeToElement(new { testEntry = new { contextId = "admitted-context" } });
        await fixture.Store.EnqueueAsync(new("test-admission", "workflow", "command", kind, "envelope", "key", now, now, payload: payload));
        var row = await fixture.Context.SchedulerWorkItems.AsNoTracking().SingleAsync();
        Assert.Equal(RuntimeSchedulerWorkEfModule.TestEntrySchemaVersion, row.SchemaVersion);
        Assert.Throws<EfSchemaVersionSkewException>(() => Predecessor(RuntimeSchedulerWorkEfModule.SchemaFamily, "3.0.0").EnsureReadable(row.SchemaVersion));
        await fixture.Context.SchedulerWorkItems.ExecuteUpdateAsync(set => set.SetProperty(x => x.SchemaVersion, "3.0.0"));
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Store.ListAsync(new("workflow")).AsTask());
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Store.ClaimAsync(new("workflow", "old-worker", now, TimeSpan.FromMinutes(1))).AsTask());
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Store.DequeueAsync("workflow").AsTask());
        row = await fixture.Context.SchedulerWorkItems.AsNoTracking().SingleAsync();
        Assert.Equal(0, row.ClaimToken);
        Assert.Equal(1, row.Revision);
    }

    [Fact]
    public async Task Test_entry_fresh_context_recovers_protected_header_and_all_native_continuations()
    {
        await using var database = await TestDatabase.CreateAsync();
        var state = TestExecution(DateTimeOffset.UtcNow);
        await using (var first = database.Open("tenant-a"))
        {
            first.Context.WorkflowExecutionStates.Add(EfWorkflowExecutionStateStore.ToEntity(state, "tenant-a",
                EfWorkflowExecutionStateStore.CreateId("tenant-a", state.WorkflowExecutionId), 1));
            await first.Context.SaveChangesAsync();
            Assert.Equal(RuntimeWorkflowExecutionEfModule.TestEntrySchemaVersion, (await first.Context.WorkflowExecutionStates.AsNoTracking().SingleAsync()).SchemaVersion);
        }
        await using var recovered = database.Open("tenant-a");
        var header = await recovered.Context.WorkflowExecutionStates.AsNoTracking().SingleAsync();
        Assert.Throws<EfSchemaVersionSkewException>(() => Predecessor(RuntimeWorkflowExecutionEfModule.SchemaFamily, "2.0.0").EnsureReadable(header.SchemaVersion));
        Assert.True(state.TestEntry!.Matches(EfWorkflowExecutionStateStore.ReadChecked(header, "tenant-a", state.WorkflowExecutionId).TestEntry));
        foreach (var kind in new[] { WorkflowExecutionCommandKind.ScheduleActivity, WorkflowExecutionCommandKind.StartActivity,
                     WorkflowExecutionCommandKind.InvokeActivity, WorkflowExecutionCommandKind.ResumeBookmark, WorkflowExecutionCommandKind.CompleteActivity,
                     WorkflowExecutionCommandKind.Checkpoint, WorkflowExecutionCommandKind.RunSchedulerWork, WorkflowExecutionCommandKind.Cancel })
        {
            var now = DateTimeOffset.UtcNow;
            await recovered.Store.EnqueueAsync(new(kind.ToString(), state.WorkflowExecutionId, kind.ToString(), kind, "envelope-" + kind,
                "key-" + kind, now, now));
        }
        Assert.All(await recovered.Context.SchedulerWorkItems.AsNoTracking().ToArrayAsync(), row =>
        {
            Assert.Equal(RuntimeSchedulerWorkEfModule.TestEntrySchemaVersion, row.SchemaVersion);
            Assert.Throws<EfSchemaVersionSkewException>(() => Predecessor(RuntimeSchedulerWorkEfModule.SchemaFamily, "3.0.0").EnsureReadable(row.SchemaVersion));
        });
        var claim = await recovered.Store.ClaimAsync(new(state.WorkflowExecutionId, "new-worker", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1)));
        Assert.NotNull(claim);
        Assert.True((await recovered.Store.ReleaseClaimAsync(claim, DateTimeOffset.UtcNow)).Succeeded);
        Assert.All(await recovered.Context.SchedulerWorkItems.AsNoTracking().ToArrayAsync(), row => Assert.Equal(RuntimeSchedulerWorkEfModule.TestEntrySchemaVersion, row.SchemaVersion));
    }

    [Theory]
    [InlineData("1.0.0")]
    [InlineData("2.0.0")]
    public async Task Test_entry_header_cannot_be_hidden_in_a_predecessor_stamp_or_have_identity_discarded(string stamp)
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        var state = TestExecution(DateTimeOffset.UtcNow);
        var row = EfWorkflowExecutionStateStore.ToEntity(state, "tenant-a", EfWorkflowExecutionStateStore.CreateId("tenant-a", state.WorkflowExecutionId), 1);
        fixture.Context.WorkflowExecutionStates.Add(row);
        await fixture.Context.SaveChangesAsync();
        Assert.Throws<InvalidOperationException>(() => EfWorkflowExecutionStateStore.CopyToEntity(row, state with { TestEntry = null }, "tenant-a", row.Id, 2));
        fixture.Context.ChangeTracker.Clear();
        await fixture.Context.WorkflowExecutionStates.ExecuteUpdateAsync(set => set.SetProperty(x => x.SchemaVersion, stamp));
        var forged = await fixture.Context.WorkflowExecutionStates.AsNoTracking().SingleAsync();
        Assert.Throws<InvalidDataException>(() => EfWorkflowExecutionStateStore.ReadChecked(forged, "tenant-a", state.WorkflowExecutionId));
        await Assert.ThrowsAsync<InvalidDataException>(() => fixture.Store.EnqueueAsync(new("resume", state.WorkflowExecutionId, "resume-command",
            WorkflowExecutionCommandKind.ResumeBookmark, "envelope", "key", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow)).AsTask());
        Assert.Empty(await fixture.Context.SchedulerWorkItems.AsNoTracking().ToArrayAsync());
        Assert.Equal(1, (await fixture.Context.WorkflowExecutionStates.AsNoTracking().SingleAsync()).Revision);
    }

    [Fact]
    public async Task Ordinary_start_authored_input_and_metadata_cannot_claim_test_format_authority()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        var now = DateTimeOffset.UtcNow;
        await fixture.Store.EnqueueAsync(new("ordinary", "workflow", "command", WorkflowExecutionCommandKind.Start, "envelope", "key", now, now,
            payload: JsonSerializer.SerializeToElement(new { inputs = new { testEntry = "authored-data" } }),
            commandMetadata: new Dictionary<string, string> { ["testEntry"] = "authored-metadata" }));
        Assert.Equal("2.0.0", (await fixture.Context.SchedulerWorkItems.AsNoTracking().SingleAsync()).SchemaVersion);
        Assert.NotNull(await fixture.Store.ClaimAsync(new("workflow", "ordinary-worker", now, TimeSpan.FromMinutes(1))));
    }

    [Fact]
    public async Task Test_entry_outbox_timer_background_and_checkpoint_retention_are_predecessor_unreadable()
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        var now = DateTimeOffset.UtcNow;
        var state = TestExecution(now);
        fixture.Context.WorkflowExecutionStates.Add(EfWorkflowExecutionStateStore.ToEntity(state, "tenant-a",
            EfWorkflowExecutionStateStore.CreateId("tenant-a", state.WorkflowExecutionId), 1));
        await fixture.Context.SaveChangesAsync();
        var access = new FixedAccessor("tenant-a");
        var outbox = new EfRuntimePostCommitOutboxStore(fixture.Context, access);
        await outbox.SavePendingAsync(new("test-send", new("send", state.WorkflowExecutionId, "test-effect", now, null, "send-key", null),
            RuntimePostCommitOutboxStatus.Pending, now, now));
        var timers = new EfDurableTimerStore(fixture.Context, access, new TestFormatCodec());
        await timers.SaveAsync(new("test-timer", state.WorkflowExecutionId, "Delay", "wait", now.AddMinutes(1), now));
        var attempt = new ActivityAttempt("attempt", "invocation", 1, ActivityAttemptReason.Initial, now);
        var work = new BackgroundActivityWorkRequest("background", state.WorkflowExecutionId, "invocation", attempt,
            state.PinnedExecutable, "selected", "contract", new("invocation", "contract", "bindings", new Dictionary<string, ValueEnvelope>(), now),
            new("coordinator", state.WorkflowExecutionId, "coordinator-command", WorkflowExecutionCommandKind.InvokeActivity, "envelope", "coordinator-key", now, now));
        fixture.Context.BackgroundActivityWorks.Add(EfBackgroundActivityWorkStore.ToEntity(BackgroundActivityWorkLifecycle.CreatePending(work, now), "tenant-a"));
        await fixture.Context.SaveChangesAsync();
        await new EfRuntimeCheckpointCommitStore(fixture.Context, access).CommitAsync(new("test-checkpoint",
            new("test-checkpoint", "TestObservation", state.WorkflowExecutionId, now, [], new Dictionary<string, string>()),
            new(null, null, [], [], [], [], []), [], new Dictionary<string, string>()), new(RuntimeCheckpointPersistenceMode.Immediate));

        var send = await fixture.Context.RuntimePostCommitOutbox.AsNoTracking().SingleAsync();
        var timer = await fixture.Context.DurableTimers.AsNoTracking().SingleAsync();
        var background = await fixture.Context.BackgroundActivityWorks.AsNoTracking().SingleAsync();
        var marker = await fixture.Context.RuntimeCheckpointCommits.AsNoTracking().SingleAsync();
        Assert.Equal(RuntimePostCommitOutboxEfModule.TestEntrySchemaVersion, send.SchemaVersion);
        Assert.Equal(RuntimeOperationalStateEfModule.TestEntrySchemaVersion, timer.SchemaVersion);
        Assert.Equal(RuntimeBackgroundActivityWorkEfModule.TestEntrySchemaVersion, background.SchemaVersion);
        Assert.Equal(RuntimeOperationalStateEfModule.TestEntrySchemaVersion, marker.SchemaVersion);
        Assert.Throws<EfSchemaVersionSkewException>(() => Predecessor(RuntimePostCommitOutboxEfModule.SchemaFamily, "2.0.0").EnsureReadable(send.SchemaVersion));
        Assert.Throws<EfSchemaVersionSkewException>(() => Predecessor(RuntimeOperationalStateEfModule.SchemaFamily, "1.0.0").EnsureReadable(timer.SchemaVersion));
        Assert.Throws<EfSchemaVersionSkewException>(() => Predecessor(RuntimeBackgroundActivityWorkEfModule.SchemaFamily, "1.0.0").EnsureReadable(background.SchemaVersion));
        Assert.Throws<EfSchemaVersionSkewException>(() => Predecessor(RuntimeOperationalStateEfModule.SchemaFamily, "1.0.0").EnsureReadable(marker.SchemaVersion));
        Assert.NotNull(await new EfBackgroundActivityWorkStore(fixture.Context, access).ClaimNextAsync("current-worker", now, TimeSpan.FromMinutes(1)));
        Assert.Equal(RuntimeBackgroundActivityWorkEfModule.TestEntrySchemaVersion, (await fixture.Context.BackgroundActivityWorks.AsNoTracking().SingleAsync()).SchemaVersion);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Test_entry_guard_respects_explicit_modified_state_when_automatic_detection_is_disabled(bool synchronous, bool testOwned)
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        var now = DateTimeOffset.UtcNow;
        var state = TestExecution(now);
        var timers = new EfDurableTimerStore(fixture.Context, new FixedAccessor("tenant-a"), new TestFormatCodec());
        await timers.SaveAsync(new("timer", state.WorkflowExecutionId, "Delay", "wait", now.AddMinutes(1), now));
        var timer = await fixture.Context.DurableTimers.SingleAsync();
        Assert.Equal("1.0.0", timer.SchemaVersion);
        fixture.Context.ChangeTracker.AutoDetectChangesEnabled = false;
        timer.Revision++;
        fixture.Context.Entry(timer).State = EntityState.Modified;
        if (testOwned)
            fixture.Context.WorkflowExecutionStates.Add(EfWorkflowExecutionStateStore.ToEntity(state, "tenant-a",
                EfWorkflowExecutionStateStore.CreateId("tenant-a", state.WorkflowExecutionId), 1));
        if (synchronous) fixture.Context.SaveChanges();
        else await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        var saved = await fixture.Context.DurableTimers.AsNoTracking().SingleAsync();
        Assert.Equal(2, saved.Revision);
        Assert.Equal(testOwned ? RuntimeOperationalStateEfModule.TestEntrySchemaVersion : "1.0.0", saved.SchemaVersion);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Test_entry_guard_does_not_persist_unmarked_ordinary_mutations_with_automatic_detection_disabled(bool synchronous)
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        var now = DateTimeOffset.UtcNow;
        var timers = new EfDurableTimerStore(fixture.Context, new FixedAccessor("tenant-a"), new TestFormatCodec());
        await timers.SaveAsync(new("timer", "ordinary", "Delay", "wait", now.AddMinutes(1), now));
        var timer = await fixture.Context.DurableTimers.SingleAsync();
        fixture.Context.ChangeTracker.AutoDetectChangesEnabled = false;
        timer.Revision++;
        if (synchronous) fixture.Context.SaveChanges();
        else await fixture.Context.SaveChangesAsync();
        fixture.Context.ChangeTracker.Clear();
        var saved = await fixture.Context.DurableTimers.AsNoTracking().SingleAsync();
        Assert.Equal(1, saved.Revision);
        Assert.Equal("1.0.0", saved.SchemaVersion);
    }

    [Theory]
    [InlineData("context")]
    [InlineData("fingerprint")]
    [InlineData("node")]
    [InlineData("pin")]
    [InlineData("scope")]
    [InlineData("authority")]
    public async Task Test_entry_header_rejects_replacement_of_each_admitted_identity(string changed)
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        var state = TestExecution(DateTimeOffset.UtcNow);
        var row = EfWorkflowExecutionStateStore.ToEntity(state, "tenant-a", EfWorkflowExecutionStateStore.CreateId("tenant-a", state.WorkflowExecutionId), 1);
        fixture.Context.WorkflowExecutionStates.Add(row);
        await fixture.Context.SaveChangesAsync();
        var entry = state.TestEntry!;
        var replacement = new WorkflowTestEntry(changed == "context" ? "other" : entry.ContextId,
            changed == "fingerprint" ? "other" : entry.ContextFingerprint,
            changed == "pin" ? new("other", "definition", "version", "1", "hash") : entry.Executable,
            changed == "node" ? "other" : entry.ExecutableNodeId,
            changed == "scope" ? new("other", entry.Scope.ExpiresAt, null, new("tenant-a")) : entry.Scope,
            changed == "authority" ? new("other", "tests") : entry.Authority);
        Assert.Throws<InvalidOperationException>(() => EfWorkflowExecutionStateStore.CopyToEntity(row,
            state with { TestEntry = replacement }, "tenant-a", row.Id, 2));
        Assert.Equal(1, row.Revision);
        Assert.True(entry.Matches(EfWorkflowExecutionStateStore.ReadChecked(row, "tenant-a", state.WorkflowExecutionId).TestEntry));
    }

    [Theory]
    [InlineData("pin")]
    [InlineData("authority")]
    [InlineData("scope")]
    public async Task Test_entry_recovery_rejects_current_format_header_inconsistent_with_its_entry(string changed)
    {
        await using var database = await TestDatabase.CreateAsync();
        await using var fixture = database.Open("tenant-a");
        var state = TestExecution(DateTimeOffset.UtcNow);
        var row = EfWorkflowExecutionStateStore.ToEntity(state, "tenant-a", EfWorkflowExecutionStateStore.CreateId("tenant-a", state.WorkflowExecutionId), 1);
        fixture.Context.WorkflowExecutionStates.Add(row);
        await fixture.Context.SaveChangesAsync();
        var entry = state.TestEntry!;
        var corrupt = new WorkflowTestEntry(entry.ContextId, entry.ContextFingerprint,
            changed == "pin" ? new("other", "definition", "version", "1", "hash") : entry.Executable,
            entry.ExecutableNodeId, changed == "scope" ? new("other", entry.Scope.ExpiresAt, null, new("tenant-a")) : entry.Scope,
            changed == "authority" ? new("other", "tests") : entry.Authority);
        // Corrupt persisted content directly, retaining otherwise valid current projections and physical format.
        row.ContentJson = RuntimeArtifactJson.Serialize(state with { TestEntry = corrupt });
        await fixture.Context.SaveChangesAsync();
        await using var reopened = database.Open("tenant-a");
        var stored = await reopened.Context.WorkflowExecutionStates.AsNoTracking().SingleAsync();
        Assert.Throws<InvalidDataException>(() => EfWorkflowExecutionStateStore.ReadChecked(stored, "tenant-a", state.WorkflowExecutionId));
        await Assert.ThrowsAsync<InvalidDataException>(() => reopened.Store.EnqueueAsync(new("resume", state.WorkflowExecutionId, "command",
            WorkflowExecutionCommandKind.ResumeBookmark, "envelope", "key", state.CreatedAt, state.CreatedAt)).AsTask());
        Assert.Empty(await reopened.Context.SchedulerWorkItems.AsNoTracking().ToArrayAsync());
    }

    private static EfSchemaChain Predecessor(string family, string version) => EfSchemaChain.For(
        new EfSchemaFamilyDescriptor(family, "Workflows.Runtime", version, typeof(EfSchedulerWorkQueueStoreTests).Assembly));

    private static WorkflowExecutionState TestExecution(DateTimeOffset now)
    {
        var pin = new WorkflowExecutableIdentity("test-artifact", "definition", "version", "1", "hash");
        var scope = new WorkflowTestScope("test-scope", now.AddMinutes(10), null, new("tenant-a"));
        var authority = new WorkflowExecutionAuthoritySnapshot("tests", "tests");
        return new("test-workflow", pin, WorkflowExecutionStatus.Running, null, now, now, now, null, null, null, null,
            new Dictionary<string, string>())
        {
            RunKind = WorkflowRunKind.TestRun, Partition = scope.Partition, Authority = authority, TestScope = scope,
            TestEntry = new("context", "context-fingerprint", pin, "selected", scope, authority)
        };
    }
}
