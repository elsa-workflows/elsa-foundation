using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Configuration;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
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
    public void Native_model_work_round_trips_its_exact_scope_attempt_pin_and_return_address()
    {
        var origin = new RuntimeModelNativeWorkOrigin(1, new("tenant"), "execution", new("test.model", 1), Pin,
            "incarnation", "service", "invocation", "attempt", Pin with { ArtifactId = "worker-artifact" },
            "worker", "model-commit:start", "start", "test.result", "result-input");
        var authority = WorkflowExecutableStartAuthority.FromModelWork(origin);
        var roundTrip = JsonSerializer.Deserialize<WorkflowExecutableStartAuthority>(JsonSerializer.Serialize(authority))!;
        Assert.Equal(WorkflowExecutableStartAuthorityKind.ModelWork, roundTrip.Kind);
        Assert.True(origin.IsEquivalentTo(roundTrip.ModelWork));
        Assert.Equal("result-input", roundTrip.ModelWork!.ResultInputId);
        Assert.Null(roundTrip.RetainedDependency);
        Assert.Null(roundTrip.StimulusDelivery);
        Assert.True(origin.MatchesModel(Change().State));
        var legacy = JsonSerializer.SerializeToElement(WorkflowExecutableStartAuthority.FromRetainedDependency("parent", "hash", "dispatch"),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.False(legacy.TryGetProperty("modelWork", out _));
    }

    [Fact]
    public void Model_queue_identity_preserves_the_existing_exact_UTF16_recipe()
    {
        var id = "command-\ud800";
        Assert.Equal("model-input:bc6890f494587ab76163b407d14f0562e2c3741571c5cc4665e53d2da40995e8",
            RuntimeModelExecutionCommand.CreateSchedulerWorkItemId(id));
        Assert.NotEqual(RuntimeModelExecutionCommand.CreateSchedulerWorkItemId("command-\ufffd"),
            RuntimeModelExecutionCommand.CreateSchedulerWorkItemId(id));
    }

    [Theory]
    [InlineData(RuntimeCheckpointPersistenceMode.Skip)]
    [InlineData(RuntimeCheckpointPersistenceMode.Deferred)]
    public async Task Model_owned_native_admission_always_reaches_the_physical_writer_immediately(RuntimeCheckpointPersistenceMode policy)
    {
        var store = new Writer();
        var committer = new RuntimeCheckpointCommitter(new Policy(policy), store, new AsyncLocalRuntimeExecutionOwnershipContextAccessor(), [], []);
        var admission = Admission();
        await committer.CommitAsync(admission);
        Assert.Equal(RuntimeCheckpointPersistenceMode.Immediate, store.Decision!.Mode);
        Assert.Same(admission.ExpectedModelExecution, store.Seen!.ExpectedModelExecution);
    }

    [Fact]
    public async Task Model_owned_native_admission_is_never_silently_accepted_by_legacy_or_in_memory_writers()
    {
        var legacy = new LegacyWriter();
        var committer = new RuntimeCheckpointCommitter(new Policy(RuntimeCheckpointPersistenceMode.Immediate), legacy,
            new AsyncLocalRuntimeExecutionOwnershipContextAccessor(), [], []);
        await Assert.ThrowsAsync<NotSupportedException>(() => committer.CommitAsync(Admission()).AsTask());
        Assert.False(legacy.Called);
        await Assert.ThrowsAsync<NotSupportedException>(() => new InMemoryRuntimeCheckpointCommitStore()
            .CommitAsync(Admission(), new(RuntimeCheckpointPersistenceMode.Immediate)).AsTask());
    }

    [Fact]
    public async Task Coalescing_preserves_the_native_admission_condition_and_refuses_to_fold_prior_changes_into_it()
    {
        var physical = new Writer();
        var sessions = new AsyncLocalRuntimeCoalescingSessionAccessor();
        IRuntimeCheckpointCommitStore store = new CoalescingRuntimeCheckpointCommitStore(new(physical), sessions);
        Assert.True(store.SupportsModelAdmission);
        var admission = Admission();
        await store.CommitAsync(admission, new(RuntimeCheckpointPersistenceMode.Immediate));
        Assert.Same(admission.ExpectedModelExecution, physical.Seen!.ExpectedModelExecution);
        physical.Seen = null;
        var session = new RuntimeCoalescingSession("worker", new InMemoryWorkflowSchedulerWorkQueue(), new PersistenceModeOptions());
        session.BufferDeferred(admission with { ExpectedModelExecution = null });
        using (sessions.Push(session))
            await Assert.ThrowsAsync<NotSupportedException>(() => store.CommitAsync(admission, new(RuntimeCheckpointPersistenceMode.Immediate)).AsTask());
        Assert.Null(physical.Seen);
    }

    [Theory]
    [InlineData("fence")]
    [InlineData("scope")]
    [InlineData("tenant")]
    [InlineData("authority")]
    [InlineData("parent")]
    [InlineData("checkpoint")]
    public void Model_owned_native_admission_requires_its_real_initial_scope_authority_and_fence(string changed)
    {
        var admission = Admission();
        var native = admission.StateChanges.WorkflowExecution!;
        var state = changed switch
        {
            "scope" => native.State with { Partition = new("foreign") },
            "tenant" => native.State with { TenantId = "foreign" },
            "authority" => native.State with { Authority = new("foreign", "initiator") },
            "parent" => native.State with { ParentWorkflowExecutionId = "fabricated-model-parent" },
            _ => native.State
        };
        admission = admission with
        {
            ExpectedFence = changed == "fence" ? null : admission.ExpectedFence,
            Checkpoint = changed == "checkpoint" ? admission.Checkpoint with { Name = "LaterCheckpoint" } : admission.Checkpoint,
            StateChanges = new(native with { State = state }, null, [], [], [], [], [])
        };
        Assert.Throws<InvalidOperationException>(admission.EnsureModelAdmissionShape);
    }

    [Fact]
    public void Absent_native_admission_retains_old_wire_and_a_refreshed_read_condition_does_not_change_decision_identity()
    {
        var admission = Admission();
        var ordinary = admission with { ExpectedModelExecution = null };
        Assert.False(JsonSerializer.SerializeToElement(ordinary, new JsonSerializerOptions(JsonSerializerDefaults.Web))
            .TryGetProperty("expectedModelExecution", out _));
        Assert.Equal(RuntimeCheckpointCommitFingerprint.Compute(ordinary),
            RuntimeCheckpointCommitFingerprint.Compute(ordinary with { ExpectedModelExecution = null }));
        Assert.Equal(RuntimeCheckpointCommitFingerprint.Compute(ordinary), RuntimeCheckpointCommitFingerprint.Compute(admission));
    }

    private static RuntimeCheckpointCommit Admission()
    {
        var model = Change().State;
        var native = new WorkflowExecutionState("worker", Pin, WorkflowExecutionStatus.Running, null, At, At, At.AddSeconds(1),
            null, null, null, model.TenantId, new Dictionary<string, string>()) { Partition = new(model.Scope.Value), Authority = model.Authority };
        return new("admission", new("admission", RuntimeCheckpointNames.WorkflowStarted, "worker", At.AddSeconds(1), [], new Dictionary<string, string>()),
            new(new("worker", RuntimeStateChangeOperation.Upsert, native, new Dictionary<string, string>()), null, [], [], [], [], []), [], new Dictionary<string, string>())
            { ExpectedModelExecution = model, ExpectedFence = new("worker-lease", "owner", 1) };
    }

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
        public bool SupportsModelAdmission => true;
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
