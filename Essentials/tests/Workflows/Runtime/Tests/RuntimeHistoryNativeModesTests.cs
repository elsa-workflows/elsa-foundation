using System.Text.Json;
using Elsa.Workflows.Runtime.Contracts;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.History;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>Real internal capture/coalescing/native-memory components. No callbacks, timers, external delivery or durable provider.</summary>
public sealed class RuntimeHistoryNativeModesTests
{
    private static readonly DateTimeOffset At = DateTimeOffset.UnixEpoch.AddDays(1);
    private static readonly WorkflowExecutableIdentity Executable = new("artifact", "definition", "version", "1", "hash");

    [Theory]
    [InlineData(PersistenceMode.Durable, true, 4)]
    [InlineData(PersistenceMode.Durable, false, 4)]
    [InlineData(PersistenceMode.Balanced, true, 2)]
    [InlineData(PersistenceMode.Balanced, false, 2)]
    [InlineData(PersistenceMode.Ephemeral, true, 1)]
    [InlineData(PersistenceMode.Ephemeral, false, 0)]
    public async Task Original_logical_history_survives_mode_cadence_without_forcing_extra_commits(PersistenceMode mode, bool recordCompletion, int expectedCommits)
    {
        using var f = new Fixture(recordCompletion);
        await using var drain = f.Services.GetRequiredService<IRuntimeCoalescingDrainScopeFactory>().Begin("run", new(mode, false));
        var checkpoints = new List<RuntimeCheckpointCommit> { WorkflowCommit("start", RuntimeCheckpointNames.WorkflowStarted, f.Policy, WorkflowExecutionStatus.Running, 0) };
        for (var i = 1; i <= 4; i++)
        {
            checkpoints.Add(ActivityCommit("start-" + i, "activity-" + i, ActivityExecutionStatus.Running, i * 2 - 1));
            checkpoints.Add(ActivityCommit("end-" + i, "activity-" + i, ActivityExecutionStatus.Completed, i * 2));
        }
        checkpoints.Add(WorkflowCommit("complete", RuntimeCheckpointNames.WorkflowCompleted, f.Policy, WorkflowExecutionStatus.Completed, 9));
        foreach (var checkpoint in checkpoints)
        {
            f.Clock.SetUtcNow(checkpoint.Checkpoint.OccurredAt);
            await f.Committer.CommitAsync(checkpoint);
        }
        await drain.FlushAtQuiescenceAsync();
        Assert.Equal(expectedCommits, f.Writer.ListCommits().Count);
        if (expectedCommits == 0)
        {
            Assert.Null(await f.Reader.FindHeadAsync("run"));
            Assert.Empty((await f.Reader.ReadPageAsync(new("run"))).Items);
            Assert.Null(await f.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("run"));
            Assert.Null(drain.Session.FoldHistoryBatchesWith());
            return;
        }
        var page = await f.Reader.ReadPageAsync(new("run"));
        Assert.Equal(Enumerable.Range(1, 10).Select(i => (long)i), page.Items.Select(e => e.Sequence));
        Assert.Equal(checkpoints.Select(c => c.Checkpoint.CheckpointId), page.Items.Select(e => e.CheckpointId));
        Assert.True(page.Head!.CapturedFromStart);
        Assert.False(page.Head.HasGaps);
        Assert.Equal(WorkflowExecutionStatus.Completed, (await f.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("run"))!.Status);
        Assert.All(page.Items, e => { Assert.Equal(0, e.CheckpointOrdinal); Assert.NotNull(e.PhysicalCommitId); Assert.True(e.RecordedAt >= e.OccurredAt); });
        Assert.Equal(expectedCommits, page.Items.Select(e => e.PhysicalCommitId).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(10, f.Writer.ListCommits().Sum(c => c.Commit.HistoryBatches!.Count));
        Assert.Null(drain.Session.FoldHistoryBatchesWith());
    }

    [Fact]
    public async Task A_persisted_wait_prevents_ephemeral_completion_from_discarding_its_existing_history()
    {
        using var f = new Fixture(recordCompletion: false);
        await using var drain = f.Services.GetRequiredService<IRuntimeCoalescingDrainScopeFactory>().Begin("run", new(PersistenceMode.Ephemeral, false));
        await f.Committer.CommitAsync(WorkflowCommit("start", RuntimeCheckpointNames.WorkflowStarted, f.Policy, WorkflowExecutionStatus.Running, 0));
        var bookmark = new BookmarkState("wait", "run", "activity", "node", "resume", "task", "hash", null, new Dictionary<string, string>(), At, null);
        var waiting = ActivityCommit("wait", "activity", ActivityExecutionStatus.Suspended, 0);
        waiting = waiting with
        {
            Checkpoint = waiting.Checkpoint with { Name = RuntimeCheckpointNames.BookmarkCreated },
            StateChanges = new(null, null, waiting.StateChanges.ActivityExecutions,
                [new("wait", RuntimeStateChangeOperation.Upsert, bookmark, new Dictionary<string, string>())], [], [], [])
        };
        await f.Committer.CommitAsync(waiting);
        Assert.True(drain.Session.HasDurableRecord);
        Assert.Single(f.Writer.ListCommits());
        var completed = ActivityCommit("end", "activity", ActivityExecutionStatus.Completed, 1);
        completed = completed with { StateChanges = new(null, null, completed.StateChanges.ActivityExecutions,
            [new("wait", RuntimeStateChangeOperation.Delete, bookmark, new Dictionary<string, string>())], [], [], []) };
        f.Clock.SetUtcNow(completed.Checkpoint.OccurredAt);
        await f.Committer.CommitAsync(completed);
        var finish = WorkflowCommit("complete", RuntimeCheckpointNames.WorkflowCompleted, f.Policy, WorkflowExecutionStatus.Completed, 2);
        f.Clock.SetUtcNow(finish.Checkpoint.OccurredAt);
        await f.Committer.CommitAsync(finish);
        await drain.FlushAtQuiescenceAsync();
        Assert.Equal(3, f.Writer.ListCommits().Count);
        var page = await f.Reader.ReadPageAsync(new("run"));
        Assert.Contains(page.Items, e => e.Name == "BookmarkCreated");
        Assert.Contains(page.Items, e => e.Name == "BookmarkRemoved");
        Assert.Contains(page.Items, e => e.Name == "WorkflowCompleted");
        Assert.Equal(RuntimeHistoryBookmarkStatus.Removed, (await f.Reader.FindBookmarkAsync("run", "wait"))!.Status);
    }

    [Fact]
    public async Task Reconstructed_original_raw_json_replays_once_after_a_later_terminal_checkpoint_and_option_change()
    {
        using var f = new Fixture(recordCompletion: true);
        var original = WorkflowCommit("original", RuntimeCheckpointNames.WorkflowStarted, f.Policy, WorkflowExecutionStatus.Running, 0);
        var queuedJson = JsonSerializer.Serialize(original); // Before enrichment; original input is retained independently.
        await f.Committer.CommitAsync(original);
        var terminal = WorkflowCommit("terminal", RuntimeCheckpointNames.WorkflowCompleted, f.Policy, WorkflowExecutionStatus.Completed, 1);
        f.Clock.SetUtcNow(terminal.Checkpoint.OccurredAt);
        await f.Committer.CommitAsync(terminal);
        f.Options.Enabled = false;
        var restored = JsonSerializer.Deserialize<RuntimeCheckpointCommit>(queuedJson)!;
        await f.Committer.CommitAsync(restored);
        Assert.Equal(2, f.Writer.ListCommits().Count);
        Assert.Equal(2, (await f.Reader.FindHeadAsync("run"))!.LastSequence);
        Assert.Equal(WorkflowExecutionStatus.Completed, (await f.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("run"))!.Status);
        var changed = restored with { StateChanges = new(new("run", RuntimeStateChangeOperation.Upsert,
            restored.StateChanges.WorkflowExecution!.State with { CorrelationId = "different-source" }, new Dictionary<string, string>()), null, [], [], [], [], []) };
        await Assert.ThrowsAsync<RuntimeCheckpointReplayConflictException>(async () => await f.Committer.CommitAsync(changed));
        Assert.Equal(2, f.Writer.ListCommits().Count);
        Assert.Equal(2, (await f.Reader.FindHeadAsync("run"))!.LastSequence);
    }

    [Fact]
    public async Task Legacy_queued_start_remains_uncaptured_after_the_internal_module_is_composed()
    {
        using var f = new Fixture(recordCompletion: true);
        var legacy = WorkflowCommit("legacy", RuntimeCheckpointNames.WorkflowStarted, null, WorkflowExecutionStatus.Running, 0);
        var originalFingerprint = RuntimeCheckpointCommitFingerprint.Compute(legacy);
        var queuedJson = JsonSerializer.Serialize(legacy);
        await f.Committer.CommitAsync(legacy);
        f.Options.Enabled = false;
        await f.Committer.CommitAsync(JsonSerializer.Deserialize<RuntimeCheckpointCommit>(queuedJson)!);
        var stored = Assert.Single(f.Writer.ListCommits()).Commit;
        Assert.Null(stored.HistoryBatches);
        Assert.Equal(originalFingerprint, RuntimeCheckpointCommitFingerprint.Compute(stored));
        Assert.Null(await f.Reader.FindHeadAsync("run"));
        Assert.Null((await f.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("run"))!.HistoryCapturePolicy);
    }

    [Fact]
    public async Task Disposing_an_unflushed_segment_does_not_publish_uncommitted_history_or_native_state()
    {
        using var f = new Fixture(recordCompletion: true);
        await using (var drain = f.Services.GetRequiredService<IRuntimeCoalescingDrainScopeFactory>().Begin("run", new(PersistenceMode.Ephemeral, false)))
        {
            await f.Committer.CommitAsync(WorkflowCommit("start", RuntimeCheckpointNames.WorkflowStarted, f.Policy, WorkflowExecutionStatus.Running, 0));
            await f.Committer.CommitAsync(ActivityCommit("start-activity", "activity", ActivityExecutionStatus.Running, 0));
            Assert.Empty(f.Writer.ListCommits());
            Assert.Null(await f.Reader.FindHeadAsync("run"));
        }
        Assert.Empty(f.Writer.ListCommits());
        Assert.Null(await f.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("run"));
        Assert.Null(await f.Reader.FindHeadAsync("run"));
    }

    private static RuntimeCheckpointCommit WorkflowCommit(string id, string name, RuntimeHistoryCapturePolicy? policy, WorkflowExecutionStatus status, int second)
    {
        var at = At.AddSeconds(second);
        var state = new WorkflowExecutionState("run", Executable, status, null, At, At, at, status.IsTerminal() ? at : null,
            null, null, null, new Dictionary<string, string>()) { HistoryCapturePolicy = policy, HistoryCaptureEnabledAtAdmission = policy is null ? null : true };
        return new(id, new("checkpoint-" + id, name, "run", at, [], new Dictionary<string, string>()),
            new(new("run", RuntimeStateChangeOperation.Upsert, state, new Dictionary<string, string>()), null, [], [], [], [], []), [], new Dictionary<string, string>());
    }
    private static RuntimeCheckpointCommit ActivityCommit(string id, string activityId, ActivityExecutionStatus status, int second)
    {
        var at = At.AddSeconds(second);
        var state = new ActivityExecutionState(new(activityId, "run", "node", "authored", "test", "1"), status, null, 1,
            At, At, status == ActivityExecutionStatus.Completed ? at : null, null, null, null, null,
            ActivitySchedulingProvenance.From("run", null, null, null, null, null, null, "test"), 0, [], [], 0, 0, new Dictionary<string, string>());
        return new(id, new("checkpoint-" + id, status == ActivityExecutionStatus.Completed ? RuntimeCheckpointNames.ActivityCompleted : RuntimeCheckpointNames.ActivityStarted,
            "run", at, [activityId], new Dictionary<string, string>()),
            new(null, null, [new(activityId, RuntimeStateChangeOperation.Upsert, state, new Dictionary<string, string>())], [], [], [], []), [], new Dictionary<string, string>());
    }
    private sealed class Fixture : IDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly IServiceScope _scope;
        internal IServiceProvider Services => _scope.ServiceProvider;
        internal FakeTimeProvider Clock { get; } = new(At);
        internal RuntimeHistoryOptions Options { get; } = new();
        internal RuntimeHistoryCapturePolicy Policy { get; }
        internal RuntimeCheckpointCommitter Committer => Services.GetRequiredService<RuntimeCheckpointCommitter>();
        internal InMemoryRuntimeCheckpointCommitStore Writer => Services.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>();
        internal IRuntimeHistoryStore Reader => Services.GetRequiredService<IRuntimeHistoryStore>();
        internal Fixture(bool recordCompletion)
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<TimeProvider>(Clock);
            services.AddSingleton<IPersistenceAccessContextAccessor>(new RuntimeHistoryTestAccess());
            services.AddSingleton<IWorkflowExecutableRootWriteLeaseManager>(PassThroughWorkflowExecutableRootWriteLeaseManager.Instance);
            services.AddWorkflowRuntime();
            services.Configure<RuntimeRecoveryContinuationOptions>(options => options.SigningKey = "native-modes-history-tests-signing-key-at-least-32-bytes");
            RuntimeHistoryRegistration.AddServices(services, Options);
            services.AddPersistenceModes(options => { options.MaxActivitiesPerCommit = 2; options.RecordCompletion = recordCompletion; });
            _provider = services.BuildServiceProvider(validateScopes: true);
            _scope = _provider.CreateScope();
            Policy = RuntimeHistoryRegistration.ResolvePolicy(Services, "run")!;
        }
        public void Dispose() { _scope.Dispose(); _provider.Dispose(); }
    }
}
