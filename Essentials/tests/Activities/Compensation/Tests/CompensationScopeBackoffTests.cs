using System.Text.Json;
using Elsa.Activities.Compensation.Activities;
using Elsa.Activities.Compensation.Internal;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;
using Microsoft.Extensions.Time.Testing;
using Xunit;
using static Elsa.Activities.Compensation.Tests.Support.CompensationGraph;

namespace Elsa.Activities.Compensation.Tests;

/// <summary>
/// Spec 261 FR-507 to FR-511, driven through the scope's callbacks: the wait before an automatic handler retry, its cap
/// and clamp, the due time recorded on the attempt, the delay-0 path that stages exactly what M4 staged, and scope state
/// version 5.
/// </summary>
public sealed class CompensationScopeBackoffTests
{
    private const string ScopeExecutionId = "actexec-scope";
    private static readonly DateTimeOffset Now = new(2026, 10, 20, 10, 42, 0, TimeSpan.Zero);
    private static readonly ExecutableNode Journey = ReservationJourney();

    // --- T509: the wait ---

    [Fact]
    public async Task BK0_a_zero_delay_stages_exactly_what_M4_staged()
    {
        // The M4 scope (no backoff input bound) and a scope with the default inputs bound stage byte-identical state and
        // the same immediate schedule for the retry.
        var m4 = await FirstHandlerFaultAsync(new CompensationScope { HandlerMaxAttempts = 3 });
        var defaults = await FirstHandlerFaultAsync(Scope(TimeSpan.Zero, 2, TimeSpan.FromMinutes(5)));

        Assert.Equal(m4.State!.Value.InlineValue!.Value.GetRawText(), defaults.State!.Value.InlineValue!.Value.GetRawText());
        var m4Retry = Assert.Single(m4.Requests);
        var retry = Assert.Single(defaults.Requests);
        Assert.Null(retry.NotBefore);
        Assert.Equal(m4Retry.ExecutableNodeId, retry.ExecutableNodeId);
        Assert.Equal(m4Retry.SchedulingActivityExecutionId, retry.SchedulingActivityExecutionId);
        Assert.Equal(m4Retry.Metadata, retry.Metadata);
        Assert.DoesNotContain("notBefore", defaults.State.Value.InlineValue!.Value.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("delayClamped", defaults.State.Value.InlineValue!.Value.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_waits_grow_by_the_multiplier_and_stop_at_the_cap()
    {
        // Delay 30 s, ×2, cap 90 s, five attempts: waits of 30, 60, 90 and 90 s before attempts 2 to 5.
        var clock = new FakeTimeProvider(Now);
        var scope = Scope(TimeSpan.FromSeconds(30), 2, TimeSpan.FromSeconds(90), maxAttempts: 5, clock: clock);
        var evaluation = await FirstHandlerFaultAsync(scope);
        var waits = new List<TimeSpan>();

        for (var attempt = 2; attempt <= 5; attempt++)
        {
            var request = Assert.Single(evaluation.Requests);
            waits.Add(request.NotBefore!.Value - clock.GetUtcNow());
            var running = StateOf(evaluation).Entries.Single(entry => entry.Step == "Reserve delivery").HandlerAttempts[^1];
            Assert.Equal((CompensationAttemptResult.Running, request.NotBefore), (running.Result, running.NotBefore));
            Assert.Null(running.DelayClamped);

            clock.Advance(waits[^1]);
            if (attempt < 5)
                evaluation = await FaultedAsync(scope, evaluation.State, request, $"actexec-cancel-{attempt}", $"incident-cancel-{attempt}");
        }

        Assert.Equal([TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(90), TimeSpan.FromSeconds(90)], waits);
    }

    [Theory]
    [InlineData(1, 30)]
    [InlineData(2, 60)]
    [InlineData(3, 120)]
    [InlineData(4, 240)]
    [InlineData(5, 300)]
    [InlineData(40, 300)]
    public void The_wait_after_n_failed_attempts_follows_the_formula(int failedAttempts, int expectedSeconds)
    {
        var wait = Scope(TimeSpan.FromSeconds(30), 2, TimeSpan.FromMinutes(5)).RetryWait(failedAttempts);

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), wait.Delay);
        Assert.False(wait.Clamped);
    }

    [Fact]
    public void A_multiplier_of_1_keeps_the_delay_fixed() =>
        Assert.Equal(TimeSpan.FromSeconds(30), Scope(TimeSpan.FromSeconds(30), 1, TimeSpan.FromMinutes(5)).RetryWait(4).Delay);

    [Theory]
    [InlineData(-5, 2, 300, 0)]
    [InlineData(30, 0.5, 300, 30)]
    [InlineData(30, double.NaN, 300, 30)]
    [InlineData(60, 2, 30, 60)]
    [InlineData(90000, 2, 300, 86400)]
    [InlineData(30, 2, 90000, 60)]
    public void An_out_of_range_value_is_clamped_and_the_clamp_is_recorded(double delaySeconds, double multiplier, double maxSeconds, int expectedSeconds)
    {
        var wait = Scope(TimeSpan.FromSeconds(delaySeconds), multiplier, TimeSpan.FromSeconds(maxSeconds)).RetryWait(2);

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), wait.Delay);
        Assert.True(wait.Clamped);
    }

    [Fact]
    public async Task A_clamped_wait_is_recorded_on_the_attempt()
    {
        var clock = new FakeTimeProvider(Now);
        var evaluation = await FirstHandlerFaultAsync(Scope(TimeSpan.FromDays(2), 2, TimeSpan.FromMinutes(5), clock: clock));

        var request = Assert.Single(evaluation.Requests);
        Assert.Equal(Now + CompensationScope.MaxHandlerRetryDelay, request.NotBefore);
        var running = StateOf(evaluation).Entries.Single(entry => entry.Step == "Reserve delivery").HandlerAttempts[^1];
        Assert.Equal((Now + CompensationScope.MaxHandlerRetryDelay, (bool?)true), (running.NotBefore, running.DelayClamped));
    }

    [Fact]
    public async Task A_retry_after_a_wait_keeps_the_handler_its_keys_and_its_scheduling_occurrence()
    {
        var immediate = await FirstHandlerFaultAsync(new CompensationScope { HandlerMaxAttempts = 3 });
        var delayed = await FirstHandlerFaultAsync(Scope(TimeSpan.FromSeconds(30), 2, TimeSpan.FromMinutes(5)));

        var expected = Assert.Single(immediate.Requests);
        var actual = Assert.Single(delayed.Requests);
        Assert.NotNull(actual.NotBefore);
        Assert.Equal(expected.ExecutableNodeId, actual.ExecutableNodeId);
        Assert.Equal(expected.SchedulingActivityExecutionId, actual.SchedulingActivityExecutionId);
        Assert.Equal(expected.Metadata, actual.Metadata);
        Assert.Equal(expected.SchedulingProvenance.Metadata, actual.SchedulingProvenance.Metadata);
        Assert.Equal($"{ScopeExecutionId}/step/1/compensate", actual.Metadata[CompensationScope.MetadataKeys.OperationKey]);
        Assert.Equal($"{ScopeExecutionId}/step/1", actual.Metadata[CompensationScope.MetadataKeys.TargetOperationKey]);
        Assert.Equal("2", actual.Metadata[CompensationScope.MetadataKeys.Attempt]);
        // The fault is absorbed in the same commit, and the entry stays compensating while the retry waits.
        Assert.Single(delayed.Context.GetChildFaultAbsorptionRequests());
        var entry = StateOf(delayed).Entries.Single(candidate => candidate.Step == "Reserve delivery");
        Assert.Equal(CompensationEntryStatus.Compensating, entry.Status);
        Assert.Equal(CompensationScopePhase.Compensating, StateOf(delayed).Phase);
    }

    [Fact]
    public async Task The_last_attempt_still_waits_for_an_operator_without_a_wait()
    {
        var scope = Scope(TimeSpan.FromSeconds(30), 2, TimeSpan.FromMinutes(5), maxAttempts: 2);
        var first = await FirstHandlerFaultAsync(scope);

        var last = await FaultedAsync(scope, first.State, Assert.Single(first.Requests), "actexec-cancel-2", "incident-cancel-2");

        Assert.Empty(last.Requests);
        Assert.Single(last.Context.GetChildFaultInterventionRequests());
        Assert.Equal(CompensationScopePhase.AwaitingIntervention, StateOf(last).Phase);
    }

    // --- T508: state version 5 ---

    [Fact]
    public void A_version_5_state_round_trips_with_the_due_time_and_the_clamp()
    {
        var state = CompensationScopeState.Initial with
        {
            Phase = CompensationScopePhase.Compensating,
            Entries =
            [
                new CompensationEntry("e1", "Reserve delivery", 1, CompensationEntryStatus.Compensating, "ae-1", "k-1",
                    handlerAttempts:
                    [
                        new CompensationHandlerAttempt(CompensationAttemptResult.Faulted, "ae-9", "inc-9"),
                        new CompensationHandlerAttempt(CompensationAttemptResult.Running, notBefore: Now, delayClamped: true)
                    ])
            ]
        };

        var staged = CompensationScopeStatePersister.Stage(RuntimeStructuralContinuation.Defer, state).StateUpdate!;
        Assert.Equal(5, staged.StateVersion);
        var loaded = Load(staged.Value.InlineValue!.Value, staged.StateVersion);

        var attempts = Assert.Single(loaded.Entries).HandlerAttempts;
        Assert.Equal((Now, (bool?)true), (attempts[1].NotBefore, attempts[1].DelayClamped));
        Assert.Null(attempts[0].NotBefore);
        Assert.Null(attempts[0].DelayClamped);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void An_older_state_is_upgraded_to_version_5_without_a_scheduled_retry(int version)
    {
        var payload = version == 1
            ? JsonSerializer.SerializeToElement(new
            {
                phase = "Compensating", nextStepIndex = 3, nextOrdinal = 2,
                entries = new object[] { new { id = "e1", step = "Reserve delivery", ordinal = 1, status = "Compensating", forwardActivityExecutionId = "ae-1", handlerActivityExecutionIds = Array.Empty<string>() } }
            })
            : JsonSerializer.SerializeToElement(new
            {
                phase = "Compensating", nextStepIndex = 3, nextOrdinal = 2,
                entries = new object[]
                {
                    new
                    {
                        id = "e1", step = "Reserve delivery", ordinal = 1, status = "Compensating", forwardActivityExecutionId = "ae-1", operationKey = "k-1",
                        forwardOutcome = "Completed",
                        handlerAttempts = new object[] { new { result = "Faulted", activityExecutionId = "ae-9", incidentId = "inc-9" }, new { result = "Running" } }
                    }
                }
            });

        var state = Load(payload, version);

        var entry = Assert.Single(state.Entries);
        Assert.All(entry.HandlerAttempts, attempt => Assert.Null(attempt.NotBefore));
        Assert.All(entry.HandlerAttempts, attempt => Assert.Null(attempt.DelayClamped));
        Assert.Equal(CompensationAttemptResult.Running, entry.HandlerAttempts[^1].Result);
        var rewritten = CompensationScopeStatePersister.Stage(RuntimeStructuralContinuation.Defer, state).StateUpdate!;
        Assert.Equal(5, rewritten.StateVersion);
        Assert.Equal(
            JsonSerializer.Serialize(state, new JsonSerializerOptions(JsonSerializerDefaults.Web)),
            JsonSerializer.Serialize(Load(rewritten.Value.InlineValue!.Value, rewritten.StateVersion), new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    }

    // --- Helpers ---

    private static CompensationScope Scope(TimeSpan delay, double multiplier, TimeSpan maxDelay, int maxAttempts = 3, TimeProvider? clock = null) =>
        new(clock ?? new FakeTimeProvider(Now))
        {
            HandlerMaxAttempts = maxAttempts,
            HandlerRetryDelay = delay,
            HandlerRetryBackoffMultiplier = multiplier,
            HandlerRetryMaxDelay = maxDelay
        };

    // Runs the reservation journey to Confirm order's fault, so Cancel delivery runs; its first attempt fails.
    private static async Task<Evaluation> FirstHandlerFaultAsync(CompensationScope scope)
    {
        var started = await ExecuteAsync(scope);
        var inventory = await CompletedAsync(scope, started.State, Assert.Single(started.Requests).Metadata, "actexec-reserve-inventory", "reserve-inventory");
        var delivery = await CompletedAsync(scope, inventory.State, Assert.Single(inventory.Requests).Metadata, "actexec-reserve-delivery", "reserve-delivery");
        var faulted = await FaultedAsync(scope, delivery.State, Assert.Single(delivery.Requests), "actexec-confirm-order", "incident-confirm");
        var cancel = Assert.Single(faulted.Requests);
        Assert.Equal("cancel-delivery", cancel.ExecutableNodeId);
        Assert.Null(cancel.NotBefore);
        return await FaultedAsync(scope, faulted.State, cancel, "actexec-cancel-1", "incident-cancel-1");
    }

    private static async Task<Evaluation> ExecuteAsync(CompensationScope scope)
    {
        var context = NewContext(null, new Dictionary<string, string>());
        var continuation = await ((IRuntimeStructuralActivity)scope).ExecuteStructureAsync(context);
        return new(continuation, context, context.GetChildActivityScheduleRequests().ToArray());
    }

    private static async Task<Evaluation> CompletedAsync(
        CompensationScope scope,
        RuntimeStructuralStateUpdate? state,
        IReadOnlyDictionary<string, string> metadata,
        string childExecutionId,
        string childNodeId)
    {
        var context = NewContext(state, metadata);
        var continuation = await scope.OnChildCompletedAsync(new ActivityChildCompletedContext(context, childExecutionId, childNodeId, [ActivityOutcomes.Done]));
        return new(continuation, context, context.GetChildActivityScheduleRequests().ToArray(), state);
    }

    private static async Task<Evaluation> FaultedAsync(
        CompensationScope scope,
        RuntimeStructuralStateUpdate? state,
        RuntimeChildActivityScheduleRequest child,
        string childExecutionId,
        string incidentId)
    {
        var context = NewContext(state, child.Metadata);
        var continuation = await scope.OnChildFaultedAsync(
            new ActivityChildFaultedContext(context, childExecutionId, child.ExecutableNodeId, incidentId, faultCode: "participant.fault", faultCategory: null));
        return new(continuation, context, context.GetChildActivityScheduleRequests().ToArray(), state);
    }

    private static CompensationScopeState Load(JsonElement payload, int version) =>
        CompensationScopeStatePersister.Load(ScopeState(new ActivityPrivateState(
            ScopeExecutionId,
            version,
            ValueEnvelope.Inline(new ValueTypeDescriptor(CompensationScopeStatePersister.StateTypeAlias, schemaVersion: version), payload, ValueProtectionPolicy.InstanceInline),
            "attempt-1",
            DateTimeOffset.UnixEpoch)))!;

    private static CompensationScopeState StateOf(Evaluation evaluation) =>
        CompensationScopeStatePersister.Deserialize(evaluation.State!.Value.InlineValue!.Value);

    private static SimpleActivityExecutionContext NewContext(RuntimeStructuralStateUpdate? state, IReadOnlyDictionary<string, string> commandMetadata) =>
        new(
            new CompensationScope(),
            CancellationToken.None,
            "wfexec-1",
            new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test"),
            new RuntimeSchedulerWorkItem(
                workItemId: "work-scope",
                workflowExecutionId: "wfexec-1",
                commandId: "command-scope",
                commandKind: WorkflowExecutionCommandKind.InvokeActivity,
                envelopeId: "envelope-1",
                idempotencyKey: "wfexec-1:scope",
                enqueuedAt: DateTimeOffset.UnixEpoch,
                recordedAt: DateTimeOffset.UnixEpoch,
                sequence: 1,
                payload: null,
                commandMetadata: commandMetadata,
                envelopeMetadata: new Dictionary<string, string>()),
            Journey,
            ScopeState(state is null ? null : new ActivityPrivateState(ScopeExecutionId, state.StateVersion, state.Value, "attempt-1", DateTimeOffset.UnixEpoch)));

    private static ActivityExecutionState ScopeState(ActivityPrivateState? privateState) =>
        new(
            Execution: new ActivityExecution(ScopeExecutionId, "wfexec-1", ScopeNodeId, $"authored-{ScopeNodeId}", typeof(CompensationScope).FullName!, "1.0.0"),
            Status: ActivityExecutionStatus.Running,
            SubStatus: null,
            ScheduledAt: DateTimeOffset.UnixEpoch,
            StartedAt: DateTimeOffset.UnixEpoch,
            CompletedAt: null,
            SchedulingActivityExecutionId: null,
            ParentActivityExecutionId: null,
            BranchId: null,
            IterationId: null,
            CallStackDepth: 0,
            BookmarkIds: [],
            IncidentIds: [],
            FaultCount: 0,
            AggregateFaultCount: 0,
            Metadata: new Dictionary<string, string>())
        {
            PrivateState = privateState
        };

    private sealed record Evaluation(
        RuntimeStructuralContinuation Continuation,
        SimpleActivityExecutionContext Context,
        RuntimeChildActivityScheduleRequest[] Requests,
        RuntimeStructuralStateUpdate? Previous = null)
    {
        public RuntimeStructuralStateUpdate? State => Continuation.StateUpdate ?? Previous;
    }
}
