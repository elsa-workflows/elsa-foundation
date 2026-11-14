using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class DurableTimerHistoryContractTests
{
    private static readonly DateTimeOffset At = new(2026, 11, 14, 0, 0, 0, TimeSpan.Zero);
    private static readonly RuntimeHistoryCapturePolicy Policy = new("runtime-history-v3", 3, true, DurableTimerHistoryFacts.Kinds);
    private static DurableTimer Timer(string run = "run", string id = "timer", DateTimeOffset? due = null) =>
        new(id, run, "Timer", "stimulus", due ?? At.AddMinutes(1), At,
            JsonSerializer.SerializeToElement(new { secret = "never-copy" }), new Dictionary<string, string> { ["authorization"] = "never-copy" },
            ActivityExecutionId: "activity", ExecutionScopeId: "scope");
    private static DurableTimer Captured(string run = "run", string id = "timer", DateTimeOffset? due = null)
    {
        var timer = Timer(run, id, due);
        return timer with { HistoryResponsibility = new(Policy, [DurableTimerHistoryFacts.CreateScheduled(timer, Policy)!.Event], false) };
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void Legacy_timer_wire_stays_absent_while_captured_responsibility_roundtrips(bool web)
    {
        var options = web ? new JsonSerializerOptions(JsonSerializerDefaults.Web) : new JsonSerializerOptions();
        var legacy = JsonSerializer.SerializeToElement(Timer(), options);
        Assert.False(legacy.TryGetProperty(web ? "historyResponsibility" : "HistoryResponsibility", out _));
        var original = Captured();
        var restored = JsonSerializer.Deserialize<DurableTimer>(JsonSerializer.Serialize(original, options), options)!;
        Assert.True(Policy.IsEquivalentTo(restored.HistoryResponsibility!.Policy));
        Assert.Equal(JsonSerializer.Serialize(original.HistoryResponsibility, options), JsonSerializer.Serialize(restored.HistoryResponsibility, options));
    }

    [Fact]
    public void Schedule_fact_has_stable_owner_identity_and_copies_only_safe_due_time_metadata()
    {
        var first = DurableTimerHistoryFacts.CreateScheduled(Timer(), Policy)!;
        var replay = DurableTimerHistoryFacts.CreateScheduled(Timer(), Policy)!;
        Assert.Equal(first.Event.HistoryEventId, replay.Event.HistoryEventId);
        Assert.Equal(At, first.Event.OccurredAt); Assert.Equal(0, first.Event.Sequence);
        Assert.Null(first.Event.Payload); Assert.Null(first.Event.PhysicalCommitId); Assert.Null(first.Event.RecordedAt);
        Assert.Equal(RuntimeHistoryAuthority.DeliveryObservation, first.Event.Authority);
        Assert.Equal(RuntimeHistoryEventCategory.SchedulerLifecycle, first.Event.Category);
        Assert.Equal(new[] { "history.scheduledAt", "history.timerId" }, first.Event.Metadata.Keys.Order(StringComparer.Ordinal));
        Assert.DoesNotContain("never-copy", JsonSerializer.Serialize(first));
        Assert.Equal("activity", first.Event.ActivityExecutionId); Assert.Equal("scope", first.Event.ExecutionScopeId);
        RuntimeHistorySafety.ValidateObservation(first);
    }

    [Fact]
    public void Missing_or_unadmitted_original_policy_cannot_retrofit_a_timer()
    {
        Assert.Null(DurableTimerHistoryFacts.CreateScheduled(Timer(), new("older", 1, true, ["ActivityStarted"])));
        Assert.Null(DurableTimerHistoryFacts.CreateCancelled(Timer(), At));
        Assert.Null(DurableTimerHistoryFacts.CreateDispatched(Timer(), At, BookmarkResumeDispatchStatus.Dispatched));
        Assert.Null(DurableTimerHistoryFacts.CreateRetired(Timer(), At, nameof(BookmarkResumeDispatchStatus.NotFound)));
    }

    [Theory]
    [InlineData(BookmarkResumeDispatchStatus.Dispatched)] [InlineData(BookmarkResumeDispatchStatus.Duplicate)]
    public void A_terminal_handoff_keeps_the_pinned_owner_and_its_actual_disposition(BookmarkResumeDispatchStatus status)
    {
        var timer = Captured(); var cause = new RuntimeHistoryCausation("received", "run", "delivery");
        var terminal = DurableTimerHistoryFacts.CreateDispatched(timer, At.AddMinutes(2), status, cause)!;
        var pending = new List<RuntimeHistoryEvent> { timer.HistoryResponsibility!.PendingEvents[0], terminal.Event };
        var responsibility = new DurableTimerHistoryResponsibility(Policy, pending, true); pending.Clear();
        Assert.Equal(2, responsibility.PendingEvents.Count); Assert.True(responsibility.Retired);
        Assert.Equal(status.ToString(), terminal.Event.Metadata["history.reasonCode"]);
        Assert.Equal(cause, terminal.Event.Causation);
        Assert.NotEqual(responsibility.PendingEvents[0].HistoryEventId, terminal.Event.HistoryEventId);
        RuntimeHistorySafety.ValidateObservation(terminal);
    }

    [Theory]
    [InlineData(BookmarkResumeDispatchStatus.NotFound)] [InlineData(BookmarkResumeDispatchStatus.WorkflowExecutionMissing)]
    [InlineData(BookmarkResumeDispatchStatus.ExecutableMissing)] [InlineData(BookmarkResumeDispatchStatus.Deferred)]
    public void Failed_or_unmatched_admission_is_never_a_dispatched_timer(BookmarkResumeDispatchStatus status) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => DurableTimerHistoryFacts.CreateDispatched(Captured(), At, status));

    [Theory]
    [InlineData("duplicate")] [InlineData("foreign-run")] [InlineData("foreign-timer")] [InlineData("foreign-due")]
    [InlineData("two-terminals")] [InlineData("live-terminal")]
    public void Responsibility_refuses_ambiguous_or_cross_owner_facts(string invalid)
    {
        var timer = Captured(); var scheduled = timer.HistoryResponsibility!.PendingEvents[0];
        var other = invalid switch
        {
            "foreign-run" => Captured(run: "foreign"), "foreign-timer" => Captured(id: "foreign"),
            "foreign-due" => Captured(due: At.AddHours(1)), _ => timer
        };
        var terminal = DurableTimerHistoryFacts.CreateCancelled(other, At.AddMinutes(2))!.Event;
        IReadOnlyList<RuntimeHistoryEvent> pending = invalid switch
        {
            "duplicate" => [scheduled, scheduled],
            "two-terminals" => [terminal, DurableTimerHistoryFacts.CreateDispatched(timer, At, BookmarkResumeDispatchStatus.Dispatched)!.Event],
            _ => [scheduled, terminal]
        };
        Assert.Throws<ArgumentException>(() => new DurableTimerHistoryResponsibility(Policy, pending, invalid != "live-terminal"));
    }

    [Theory]
    [InlineData("payload")] [InlineData("position")] [InlineData("identity")] [InlineData("due")]
    [InlineData("metadata")] [InlineData("category")] [InlineData("unknown-time")]
    public void Public_observation_boundary_refuses_malformed_timer_facts(string invalid)
    {
        var original = DurableTimerHistoryFacts.CreateScheduled(Timer(), Policy)!.Event;
        var metadata = original.Metadata.ToDictionary();
        if (invalid == "due") metadata["history.scheduledAt"] = "not-a-time";
        if (invalid == "metadata") metadata["history.reasonCode"] = "unauthorized-schedule-reason";
        var malformed = new RuntimeHistoryEvent(invalid == "identity" ? "rebound" : original.HistoryEventId, "run", invalid == "position" ? 1 : 0,
            invalid == "category" ? RuntimeHistoryEventCategory.DeliveryObservation : original.Category, original.Name, At,
            payload: invalid == "payload" ? JsonSerializer.SerializeToElement(new { secret = "never-copy" }) : null, metadata: metadata)
        { Authority = original.Authority, OccurredAtKnown = invalid == "unknown-time" ? false : null };
        Assert.Throws<InvalidOperationException>(() => RuntimeHistorySafety.ValidateObservation(new(Policy, malformed)));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void No_record_receipt_cannot_clear_a_live_or_still_pending_responsibility(bool retired)
    {
        var pending = Captured().HistoryResponsibility!.PendingEvents;
        Assert.Throws<ArgumentException>(() => new DurableTimerHistoryResponsibility(Policy, retired ? pending : [], retired)
            { NoRecordCompletion = new("completed-checkpoint", At) });
        var receipt = new DurableTimerHistoryResponsibility(Policy, [], true) { NoRecordCompletion = new("completed-checkpoint", At) };
        Assert.Equal("completed-checkpoint", JsonSerializer.Deserialize<DurableTimerHistoryResponsibility>(JsonSerializer.Serialize(receipt))!.NoRecordCompletion!.CheckpointId);
    }

    [Theory]
    [InlineData("fence")] [InlineData("retained-work")] [InlineData("checkpoint")] [InlineData("running")]
    [InlineData("completion-time")] [InlineData("policy")] [InlineData("mode")]
    public void Completion_shape_alone_requires_a_fenced_captured_ephemeral_completion(string invalid)
    {
        var state = CompletionState();
        state = invalid switch
        {
            "running" => state with { Status = WorkflowExecutionStatus.Running },
            "completion-time" => state with { CompletedAt = null },
            "policy" => state with { HistoryCapturePolicy = null },
            "mode" => state with { SystemMetadata = new Dictionary<string, string>() }, _ => state
        };
        var completion = Completion(state, invalid == "checkpoint" ? "ActivityCompleted" : RuntimeCheckpointNames.WorkflowCompleted) with
        { ExpectedFence = invalid == "fence" ? null : new("lease", "owner", 1), RetainSchedulerWork = invalid == "retained-work" };
        Assert.Throws<InvalidOperationException>(() => RuntimeHistoryNoRecordCompletion.Validate(completion));
    }

    [Fact]
    public void A_valid_completion_shape_is_only_input_to_the_providers_separate_native_absence_proof()
    {
        var state = CompletionState();
        var completion = Completion(state, RuntimeCheckpointNames.WorkflowCompleted) with { ExpectedFence = new("lease", "owner", 1) };
        Assert.Same(state, RuntimeHistoryNoRecordCompletion.Validate(completion));
    }

    private static WorkflowExecutionState CompletionState() => new("run", new("artifact", "definition", "version", "1", "hash"),
        WorkflowExecutionStatus.Completed, null, At, At, At, At, null, null, "tenant",
        new Dictionary<string, string> { [RuntimeMetadataKeys.PersistenceMode] = nameof(PersistenceMode.Ephemeral) }) { HistoryCapturePolicy = Policy };
    private static RuntimeCheckpointCommit Completion(WorkflowExecutionState state, string name) => new("commit",
        new("checkpoint", name, "run", At, [], new Dictionary<string, string>()),
        new(new("run", RuntimeStateChangeOperation.Upsert, state, new Dictionary<string, string>()), null, [], [], [], [], []),
        [], new Dictionary<string, string>());
}
