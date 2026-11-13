using System.Data.Common;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.History;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeHistorySourceOwnerTests
{
    private static readonly DateTimeOffset At = DateTimeOffset.UnixEpoch;
    private static readonly RuntimeHistoryCapturePolicy Policy = new("runtime-history-v3", 3, true, ["IncidentRecorded"]);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Ordinary_composition_resolves_the_source_lazily_without_changing_default_history_behavior(bool installed)
    {
        var executions = new InMemoryWorkflowExecutionStateStore();
        await executions.SaveAsync(Workflow(Policy));
        var services = new ServiceCollection();
        services.AddWorkflowRuntime();
        services.AddSingleton<IWorkflowExecutionStateStore>(executions);
        if (installed) services.AddScoped<IRuntimeHistorySourceExtension, DefaultRuntimeHistorySourceExtension>();
        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();
        var committer = scope.ServiceProvider.GetRequiredService<RuntimeCheckpointCommitter>();
        var commit = Commit();
        var calls = 0;
        var result = await committer.AddHistorySourceFactsAsync(commit, () => { calls++; return [Fact("subject")]; });
        Assert.Equal(installed ? 1 : 0, calls);
        if (installed) Assert.Equal("subject", Assert.Single(result.HistorySourceFacts!).SubjectId);
        else Assert.Same(commit, result);
        Assert.Null(scope.ServiceProvider.GetService<RuntimeHistoryOptions>());
        Assert.Null(scope.ServiceProvider.GetService<IRuntimeHistoryStore>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Native_choice_including_absence_wins_and_only_a_missing_native_row_uses_proposed_state(bool nativeExists)
    {
        var executions = new InMemoryWorkflowExecutionStateStore();
        if (nativeExists) await executions.SaveAsync(Workflow(null));
        var commit = Commit() with
        {
            StateChanges = new(new("run", RuntimeStateChangeOperation.Upsert, Workflow(Policy), new Dictionary<string, string>()), null, [], [], [], [], [])
        };
        var calls = 0;
        var result = await new DefaultRuntimeHistorySourceExtension(executions).AddFactsAsync(commit,
            _ => { calls++; return [Fact("subject")]; });
        Assert.Equal(nativeExists ? 0 : 1, calls);
        if (nativeExists) Assert.Same(commit, result);
        else Assert.Single(result.HistorySourceFacts!);
    }

    [Fact]
    public async Task Older_policy_never_activates_a_factory_or_rewrites_its_prior_source()
    {
        var executions = new InMemoryWorkflowExecutionStateStore();
        await executions.SaveAsync(Workflow(new("runtime-history-v2", 2, true, ["IncidentRecorded"])));
        var commit = Commit() with { HistorySourceFacts = [Fact("prior")] };
        var result = await new DefaultRuntimeHistorySourceExtension(executions).AddFactsAsync(commit, _ => throw new InvalidOperationException("must stay lazy"));
        Assert.Same(commit, result);
    }

    [Fact]
    public async Task Wide_owner_input_keeps_order_and_omitted_vocabulary_kinds_in_its_original_logical_group()
    {
        var source = await Source();
        var supplied = Enumerable.Range(0, 600).Select(index => Fact($"subject-{index}")).ToArray();
        var first = await source.AddFactsAsync(Commit(), _ => supplied);
        var replay = await source.AddFactsAsync(first, _ => supplied);
        Assert.Equal(supplied, replay.HistorySourceFacts);
        Assert.Equal(RuntimeCheckpointCommitFingerprint.Compute(first), RuntimeCheckpointCommitFingerprint.Compute(replay));
        Assert.Equal("IncidentRecorded", Assert.Single(Policy.SupportedKinds));
        Assert.All(replay.HistorySourceFacts!, fact => Assert.Equal(RuntimeHistorySourceFactKind.ControlApplied, fact.Kind));
    }

    [Theory]
    [InlineData("time-offset")]
    [InlineData("cause")]
    [InlineData("outcome-order")]
    [InlineData("unicode")]
    public async Task Equivalent_replay_deduplicates_but_distinct_owner_inputs_cannot_alias_through_serialization(string conflict)
    {
        var source = await Source();
        var original = Fact("subject", reason: "\ud800", outcomes: ["A", "B"]);
        var first = await source.AddFactsAsync(Commit(), _ => [original]);
        var copy = Fact("subject", reason: "\ud800", outcomes: ["A", "B"]);
        Assert.Single((await source.AddFactsAsync(first, _ => [copy])).HistorySourceFacts!);
        var changed = conflict switch
        {
            "time-offset" => Fact("subject", At.ToOffset(TimeSpan.FromHours(1)), reason: "\ud800", outcomes: ["A", "B"]),
            "cause" => Fact("subject", reason: "\ud800", outcomes: ["A", "B"], cause: new(CommandId: "other")),
            "outcome-order" => Fact("subject", reason: "\ud800", outcomes: ["B", "A"]),
            _ => Fact("subject", reason: "\ud801", outcomes: ["A", "B"])
        };
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await source.AddFactsAsync(first, _ => [changed]));
        Assert.Same(original, Assert.Single(first.HistorySourceFacts!));
    }

    [Fact]
    public async Task Untouched_duplicate_prior_proof_is_preserved_but_cannot_be_ambiguously_recontributed()
    {
        var source = await Source();
        var duplicate = Fact("prior");
        var commit = Commit() with { HistorySourceFacts = [duplicate, duplicate] };
        var result = await source.AddFactsAsync(commit, _ => [Fact("new")]);
        Assert.Equal(new[] { "prior", "prior", "new" }, result.HistorySourceFacts!.Select(fact => fact.SubjectId));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await source.AddFactsAsync(result, _ => [duplicate]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Wrapped_provider_outage_defers_while_corruption_remains_hard_without_activating_the_owner(bool malformed)
    {
        var failure = new InvalidOperationException("provider context", malformed ? new InvalidDataException("corrupt") : new TestDbException());
        var source = new DefaultRuntimeHistorySourceExtension(new FailingStore(failure));
        var calls = 0;
        var caught = await Record.ExceptionAsync(async () => await source.AddFactsAsync(Commit(), _ => { calls++; return []; }));
        if (malformed) Assert.Same(failure, Assert.IsType<RuntimeHistoryIntegrityException>(caught).InnerException);
        else Assert.Same(failure, Assert.IsType<RuntimeSchedulerWorkUnavailableException>(caught).InnerException);
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task Cancellation_during_policy_read_remains_the_original_failure()
    {
        using var cancellation = new CancellationTokenSource();
        var failure = new InvalidOperationException("provider context", new OperationCanceledException(cancellation.Token));
        var source = new DefaultRuntimeHistorySourceExtension(new FailingStore(failure, cancellation.Cancel));
        var caught = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await source.AddFactsAsync(Commit(), _ => throw new InvalidOperationException("must not run"), cancellation.Token));
        Assert.Same(failure, caught);
    }

    [Fact]
    public void Incident_facts_distinguish_actual_decision_times_from_unavailable_repair_timing()
    {
        var applied = At.AddMinutes(1);
        var repaired = At.AddMinutes(2);
        var incident = Incident(IncidentStatus.Resolved, new("Custom.Resolve", applied, null, "test"), applied);
        var facts = new[]
        {
            RuntimeHistoryIncidentFacts.Created(incident), RuntimeHistoryIncidentFacts.StrategyEvaluated(incident),
            RuntimeHistoryIncidentFacts.DecisionApplied(incident), RuntimeHistoryIncidentFacts.Resolved(incident),
            RuntimeHistoryIncidentFacts.ProjectionReconciled(incident, repaired)
        };
        Assert.Equal(new[] { At, applied, applied, applied, repaired }, facts.Select(fact => fact.OccurredAt));
        Assert.Equal(5, facts.Select(fact => fact.Kind).Distinct().Count());
        Assert.All(facts, fact => { Assert.Equal("incident", fact.IncidentId); Assert.Equal("activity", fact.ActivityExecutionId); });
        Assert.Null(facts[0].ReasonCode);
        Assert.Equal("Custom.Resolve", facts[1].ReasonCode);
        Assert.Null(facts[4].ReasonCode);
        Assert.False(facts[4].OccurredAtKnown);
        Assert.All(facts.Take(4), fact => Assert.Null(fact.OccurredAtKnown));
        var reconstructedRepair = RuntimeHistoryIncidentFacts.ProjectionReconciled(incident, repaired);
        Assert.True(facts[4].IsEquivalentTo(reconstructedRepair));
        Assert.False(facts[4].IsEquivalentTo(reconstructedRepair with { OccurredAtKnown = true }));
    }

    [Fact]
    public void Long_native_action_kind_remains_valid_without_exceeding_the_diagnostic_label_budget()
    {
        var incident = Incident(IncidentStatus.Blocking, new(new string('A', 512), At, null, "test"));
        Assert.Null(RuntimeHistoryIncidentFacts.StrategyEvaluated(incident).ReasonCode);
        Assert.Null(RuntimeHistoryIncidentFacts.DecisionApplied(incident).ReasonCode);
        Assert.Equal(512, incident.ResolutionOutcome!.ActionKind.Length);
    }

    [Fact]
    public void An_unapplied_or_different_terminal_state_cannot_claim_resolution_or_suppression()
    {
        var blocking = Incident(IncidentStatus.Blocking);
        Assert.Throws<ArgumentException>(() => RuntimeHistoryIncidentFacts.Resolved(blocking));
        Assert.Throws<ArgumentException>(() => RuntimeHistoryIncidentFacts.StrategyEvaluated(blocking));
        var resolved = Incident(IncidentStatus.Resolved, new("resolve", At, null, "test"), At);
        Assert.Throws<ArgumentException>(() => RuntimeHistoryIncidentFacts.Suppressed(resolved));
        var suppressed = Incident(IncidentStatus.Suppressed, new("suppress", At, null, "test"), At);
        Assert.Equal(RuntimeHistorySourceFactKind.IncidentSuppressed, RuntimeHistoryIncidentFacts.Suppressed(suppressed).Kind);
        Assert.Throws<ArgumentException>(() => RuntimeHistoryIncidentFacts.Resolved(suppressed));
    }

    [Fact]
    public void Unavailable_occurrence_time_survives_roundtrip_positioning_and_identity_without_changing_old_envelopes()
    {
        RuntimeHistoryEvent Event(bool? known) => new("event", "run", 0, RuntimeHistoryEventCategory.IncidentLifecycle,
            "IncidentProjectionReconciled", At) { OccurredAtKnown = known };
        var unknown = Event(false);
        var positioned = RuntimeHistoryIdentity.Position(unknown, 1, "physical", At.AddMinutes(5));
        Assert.False(positioned.OccurredAtKnown);
        Assert.Equal(At.AddMinutes(5), positioned.RecordedAt);
        Assert.Equal(RuntimeHistoryIdentity.Compute(unknown), RuntimeHistoryIdentity.Compute(positioned));
        Assert.NotEqual(RuntimeHistoryIdentity.Compute(unknown), RuntimeHistoryIdentity.Compute(Event(null)));
        Assert.False(JsonSerializer.SerializeToElement(Event(null)).TryGetProperty("OccurredAtKnown", out _));
        var restored = JsonSerializer.Deserialize<RuntimeHistoryEvent>(JsonSerializer.Serialize(positioned))!;
        Assert.False(restored.OccurredAtKnown);
        Assert.Equal(RuntimeHistoryIdentity.Compute(positioned), RuntimeHistoryIdentity.Compute(restored));
        var fact = RuntimeHistoryIncidentFacts.ProjectionReconciled(Incident(IncidentStatus.Blocking), At);
        var restoredFact = JsonSerializer.Deserialize<RuntimeHistorySourceFact>(JsonSerializer.Serialize(fact))!;
        Assert.True(fact.IsEquivalentTo(restoredFact));
        Assert.NotEqual(RuntimeCheckpointCommitFingerprint.Compute(Commit() with { HistorySourceFacts = [fact] }),
            RuntimeCheckpointCommitFingerprint.Compute(Commit() with { HistorySourceFacts = [fact with { OccurredAtKnown = true }] }));
    }

    private static IncidentState Incident(IncidentStatus status, IncidentResolutionOutcome? outcome = null, DateTimeOffset? resolved = null) =>
        new("incident", "run", "activity", "node", IncidentSeverity.Error, status, outcome, "failure", "private failure detail", At, resolved);

    private static async Task<DefaultRuntimeHistorySourceExtension> Source()
    {
        var executions = new InMemoryWorkflowExecutionStateStore();
        await executions.SaveAsync(Workflow(Policy));
        return new(executions);
    }

    private static RuntimeHistorySourceFact Fact(string id, DateTimeOffset? at = null, string? reason = null,
        IReadOnlyList<string>? outcomes = null, RuntimeHistoryCausation? cause = null) =>
        new(RuntimeHistorySourceFactKind.ControlApplied, id, at ?? At, "activity", causation: cause, reasonCode: reason, outcomeNames: outcomes);

    private static RuntimeCheckpointCommit Commit() => new("commit", new("checkpoint", "test", "run", At, [], new Dictionary<string, string>()),
        new(null, null, [], [], [], [], []), [], new Dictionary<string, string>());

    private static WorkflowExecutionState Workflow(RuntimeHistoryCapturePolicy? policy) => new("run", new("artifact", "definition", "version", "1", "hash"),
        WorkflowExecutionStatus.Running, null, At, At, At, null, null, null, null, new Dictionary<string, string>()) { HistoryCapturePolicy = policy };

    private sealed class TestDbException() : DbException("provider unavailable") { }
    private sealed class FailingStore(Exception failure, Action? beforeFailure = null) : IWorkflowExecutionStateStore
    {
        public ValueTask<WorkflowExecutionState?> FindAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
        { beforeFailure?.Invoke(); throw failure; }
        public ValueTask<WorkflowExecutionState> SaveAsync(WorkflowExecutionState state, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyCollection<WorkflowExecutionState>> ListAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<WorkflowExecutionStatePage> QueryPageAsync(WorkflowExecutionStatePageQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyCollection<string>> ListPinnedExecutableArtifactIdsAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<bool> DeleteAsync(string workflowExecutionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
