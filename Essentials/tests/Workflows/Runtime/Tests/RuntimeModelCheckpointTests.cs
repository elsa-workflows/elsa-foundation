using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Configuration;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Coalescing;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeModelCheckpointTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);
    private static readonly WorkflowExecutableIdentity Pin = new("artifact", "definition", "version", "1.0.0", "hash");

    [Fact]
    public void Model_state_and_input_keep_detached_payloads_and_round_trip_without_native_execution()
    {
        var change = Change();
        var roundTrip = JsonSerializer.Deserialize<RuntimeModelStateChange>(JsonSerializer.Serialize(change))!;
        Assert.Equal("human", roundTrip.State.Payload.GetProperty("waiting").GetString());
        Assert.Equal(Pin, roundTrip.State.Executable);
        Assert.Equal(change.State.Authority.SystemIdentity, roundTrip.State.Authority.SystemIdentity);
        Assert.Equal("accepted", roundTrip.Input.Result.GetString());
        Assert.Equal(change.State.Scope, roundTrip.Input.Scope);
        Assert.Equal(change.State.Revision, roundTrip.Input.AcceptedRevision);
    }

    [Fact]
    public void A_model_decision_participates_in_replay_identity_but_absence_retains_native_wire()
    {
        var native = Commit() with { ModelState = null };
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Assert.False(JsonSerializer.SerializeToElement(native, options).TryGetProperty("modelState", out _));
        Assert.Equal(RuntimeCheckpointCommitFingerprint.Compute(native), RuntimeCheckpointCommitFingerprint.Compute(native with { ModelState = null }));
        Assert.NotEqual(RuntimeCheckpointCommitFingerprint.Compute(native), RuntimeCheckpointCommitFingerprint.Compute(Commit()));
        Assert.NotEqual(RuntimeCheckpointCommitFingerprint.Compute(Commit()),
            RuntimeCheckpointCommitFingerprint.Compute(Commit() with { ModelState = Change("different") }));
    }

    [Theory]
    [InlineData(RuntimeCheckpointPersistenceMode.Skip)]
    [InlineData(RuntimeCheckpointPersistenceMode.Deferred)]
    public async Task The_committer_forces_a_model_decision_durable(RuntimeCheckpointPersistenceMode policy)
    {
        var store = new Writer();
        var committer = new RuntimeCheckpointCommitter(new Policy(policy), store, new AsyncLocalRuntimeExecutionOwnershipContextAccessor(), [], []);
        var commit = Commit();
        await committer.CommitAsync(commit);
        Assert.Equal(RuntimeCheckpointPersistenceMode.Immediate, store.Decision!.Mode);
        Assert.Same(commit.ModelState, store.Seen!.ModelState);
    }

    [Fact]
    public async Task Legacy_providers_refuse_before_a_model_decision_can_be_lost()
    {
        var legacy = new LegacyWriter();
        var committer = new RuntimeCheckpointCommitter(new Policy(RuntimeCheckpointPersistenceMode.Immediate), legacy,
            new AsyncLocalRuntimeExecutionOwnershipContextAccessor(), [], []);
        await Assert.ThrowsAsync<NotSupportedException>(() => committer.CommitAsync(Commit()).AsTask());
        Assert.False(legacy.Called);
        await Assert.ThrowsAsync<NotSupportedException>(() => new InMemoryRuntimeCheckpointCommitStore()
            .CommitAsync(Commit(), new(RuntimeCheckpointPersistenceMode.Immediate)).AsTask());
    }

    [Fact]
    public async Task The_coalescing_decorator_preserves_a_physical_decision_and_refuses_a_native_working_set()
    {
        var physical = new Writer();
        var sessions = new AsyncLocalRuntimeCoalescingSessionAccessor();
        IRuntimeCheckpointCommitStore store = new CoalescingRuntimeCheckpointCommitStore(new(physical), sessions);
        var commit = Commit();
        await store.CommitAsync(commit, new(RuntimeCheckpointPersistenceMode.Immediate));
        Assert.Same(commit.ModelState, physical.Seen!.ModelState);
        physical.Seen = null;
        var session = new RuntimeCoalescingSession("execution", new InMemoryWorkflowSchedulerWorkQueue(), new PersistenceModeOptions());
        using (sessions.Push(session))
            await Assert.ThrowsAsync<NotSupportedException>(() => store.CommitAsync(commit, new(RuntimeCheckpointPersistenceMode.Immediate)).AsTask());
        Assert.Null(physical.Seen);
    }

    [Fact]
    public void A_model_decision_requires_its_fence_and_exact_checkpoint_timestamp()
    {
        Assert.Throws<InvalidOperationException>(() => (Commit() with { ExpectedFence = null }).EnsureModelStateShape());
        Assert.Throws<InvalidOperationException>(() => (Commit() with { Checkpoint = Commit().Checkpoint with { OccurredAt = At.AddSeconds(1) } }).EnsureModelStateShape());
        Assert.Throws<InvalidOperationException>(() => (Commit() with { Checkpoint = Commit().Checkpoint with { WorkflowExecutionId = "other" } }).EnsureModelStateShape());
    }

    private static RuntimeCheckpointCommit Commit() => new("commit", new("checkpoint", "ModelDecision", "execution", At, [], new Dictionary<string, string>()),
        new(null, null, [], [], [], [], []), [], new Dictionary<string, string>())
        { ModelState = Change(), ExpectedFence = new("lease", "owner", 1) };

    private static RuntimeModelStateChange Change(string result = "accepted")
    {
        using var payload = JsonDocument.Parse("{\"waiting\":\"human\"}");
        using var outcome = JsonDocument.Parse(JsonSerializer.Serialize(result));
        var state = new RuntimeModelExecutionState(1, new("tenant"), "execution", "tenant", new("test.model", 1), Pin,
            "incarnation", 1, RuntimeModelLifecycle.Running, new("system", "initiator"), At, At, payload.RootElement, null);
        return new(null, state, new(new("tenant"), "execution", "input", "start", "fingerprint", 1, At, null, null, outcome.RootElement));
    }

    private sealed class Policy(RuntimeCheckpointPersistenceMode mode) : IRuntimeCheckpointPersistencePolicy
    {
        public ValueTask<RuntimeCheckpointPersistenceDecision> DecideAsync(RuntimeCheckpoint checkpoint, CancellationToken cancellationToken = default) => ValueTask.FromResult(new RuntimeCheckpointPersistenceDecision(mode));
    }

    private sealed class Writer : IRuntimeCheckpointCommitStore
    {
        public bool SupportsModelCheckpoint => true;
        public RuntimeCheckpointCommit? Seen { get; set; }
        public RuntimeCheckpointPersistenceDecision? Decision { get; private set; }
        public ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(RuntimeCheckpointCommit commit, RuntimeCheckpointPersistenceDecision decision, CancellationToken cancellationToken = default)
        {
            Seen = commit; Decision = decision;
            return ValueTask.FromResult(new RuntimeCheckpointCommitStoreResult([]));
        }
    }
    private sealed class LegacyWriter : IRuntimeCheckpointCommitStore
    {
        public bool Called { get; private set; }
        public ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(RuntimeCheckpointCommit commit, RuntimeCheckpointPersistenceDecision decision, CancellationToken cancellationToken = default)
        { Called = true; return ValueTask.FromResult(new RuntimeCheckpointCommitStoreResult([])); }
    }
}
