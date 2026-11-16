using System.Text.Json;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Elsa.Workflows.Runtime.Services.Alterations;
using Elsa.Workflows.Runtime.Services.Alterations.Handlers;
using Elsa.Workflows.Runtime.Services.Executables;
using Elsa.Workflows.Runtime.Services.Incidents;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests.Alterations;

/// <summary>
/// <c>DecideIntervention/1</c> preflight (spec 260 FR-402): every refusal in the contract's order, and the accepted path
/// that stages exactly one resolved incident and one durable delivery to the requester.
/// </summary>
public sealed class DecideInterventionAlterationHandlerTests
{
    private const string Resolve = "test.resolve.effect-absent";
    private const string Abandon = "test.abandon";
    private static readonly DateTimeOffset IncidentCreatedAt = DateTimeOffset.UnixEpoch;

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"incidentId":"incident","decision":"test.abandon"}""")]
    [InlineData("""{"incidentId":"incident","decision":"test.abandon","reason":" "}""")]
    [InlineData("""{"incidentId":"incident","decision":"test.abandon","reason":"line\nbreak"}""")]
    [InlineData("""{"incidentId":"incident","decision":"test.abandon","reason":"ok","extra":true}""")]
    [InlineData("""{"incidentId":42,"decision":"test.abandon","reason":"ok"}""")]
    [InlineData("""{"incidentId":"incident","decision":null,"reason":"ok"}""")]
    [InlineData("""{"incidentId":" ","decision":"test.abandon","reason":"ok"}""")]
    [InlineData("[]")]
    public async Task PreflightAsync_RejectsMalformedPayloads(string payload)
    {
        var fixture = await Fixture.CreateAsync();
        var staging = new Staging();

        var result = await fixture.Handler.PreflightAsync(fixture.Context(JsonDocument.Parse(payload).RootElement.Clone(), staging));

        Assert.Equal("InvalidDecideInterventionPayload", result.Failure!.Code);
        Assert.Empty(staging.StagedChanges);
    }

    [Fact]
    public async Task PreflightAsync_RejectsAReasonLongerThanTheLimit()
    {
        var fixture = await Fixture.CreateAsync();

        var result = await fixture.Handler.PreflightAsync(fixture.Context(Payload(reason: new string('r', DecideInterventionAlterationHandler.MaxReasonLength + 1)), new Staging()));

        Assert.Equal("InvalidDecideInterventionPayload", result.Failure!.Code);
    }

    [Fact]
    public async Task PreflightAsync_AcceptsAReasonAtTheLimit()
    {
        var fixture = await Fixture.CreateAsync();

        var result = await fixture.Handler.PreflightAsync(fixture.Context(Payload(reason: new string('r', DecideInterventionAlterationHandler.MaxReasonLength)), new Staging()));

        Assert.True(result.IsAccepted, result.Failure?.Message);
    }

    [Theory]
    [InlineData(WorkflowExecutionStatus.Faulted)]
    [InlineData(WorkflowExecutionStatus.Completed)]
    [InlineData(WorkflowExecutionStatus.Cancelled)]
    public async Task PreflightAsync_RejectsATerminalWorkflow(WorkflowExecutionStatus status)
    {
        var fixture = await Fixture.CreateAsync();

        var result = await fixture.Handler.PreflightAsync(fixture.Context(Payload(), new Staging(), workflow: Workflow(status)));

        Assert.Equal("WorkflowTerminal", result.Failure!.Code);
    }

    [Fact]
    public async Task PreflightAsync_RejectsARunWhoseAuthorityChangedAfterCapture()
    {
        var fixture = await Fixture.CreateAsync();
        var job = new WorkflowAlterationJobState("job", "plan", "workflow", "tenant", 0, WorkflowAlterationJobStatus.Running,
            new WorkflowAlterationJobClaim("worker", "claim", DateTimeOffset.MaxValue), 1, [], null, null, DateTimeOffset.UnixEpoch, null, null, 0,
            new WorkflowAlterationCapturedConcurrency(
                null, null, Workflow().PinnedExecutable, new Dictionary<string, string>(),
                new WorkflowExecutionAuthoritySnapshot("system", "captured-root")));

        var result = await fixture.Handler.PreflightAsync(fixture.Context(Payload(), new Staging(), job: job));

        Assert.Equal("ExecutionAuthorityConflict", result.Failure!.Code);
    }

    [Fact]
    public async Task PreflightAsync_RejectsAMissingIncident()
    {
        var fixture = await Fixture.CreateAsync();

        var result = await fixture.Handler.PreflightAsync(fixture.Context(Payload(incidentId: "missing"), new Staging()));

        Assert.Equal("IncidentNotFound", result.Failure!.Code);
    }

    [Fact]
    public async Task PreflightAsync_RejectsAnIncidentOfAnotherRun()
    {
        var fixture = await Fixture.CreateAsync();
        await fixture.Incidents.SaveAsync(WaitingIncident("foreign", workflowExecutionId: "other-workflow"));

        var result = await fixture.Handler.PreflightAsync(fixture.Context(Payload(incidentId: "foreign"), new Staging()));

        Assert.Equal("IncidentNotFound", result.Failure!.Code);
    }

    public static TheoryData<string> NotPendingIncidents() => ["resolved", "no-outcome", "strategy-wait", "open"];

    [Theory]
    [MemberData(nameof(NotPendingIncidents))]
    public async Task PreflightAsync_RejectsAnIncidentThatNoLongerWaitsForAnOperator(string shape)
    {
        IncidentState incident = shape switch
        {
            "resolved" => new IncidentState(
                "incident", "workflow", "faulted", "faulted-node", IncidentSeverity.Error, IncidentStatus.Resolved,
                new IncidentResolutionOutcome(IncidentResolutionActionKinds.OperatorRetry, IncidentCreatedAt, null, IncidentResolutionSystemSources.OperatorRetryAlteration),
                "TestFailure", "The activity faulted.", IncidentCreatedAt, IncidentCreatedAt),
            "no-outcome" => new IncidentState(
                "incident", "workflow", "faulted", "faulted-node", IncidentSeverity.Error, IncidentStatus.Blocking, null,
                "TestFailure", "The activity faulted.", IncidentCreatedAt, null),
            // A wait the run's incident strategy owns offers nothing to decide.
            "strategy-wait" => new IncidentState(
                "incident", "workflow", "faulted", "faulted-node", IncidentSeverity.Error, IncidentStatus.Blocking,
                new IncidentResolutionOutcome(IncidentResolutionActionKinds.WaitForIntervention, IncidentCreatedAt, new IncidentStrategyReference("Wait", "1"), systemSource: null,
                    metadata: OfferMetadata()),
                "TestFailure", "The activity faulted.", IncidentCreatedAt, null),
            _ => new IncidentState(
                "incident", "workflow", "faulted", "faulted-node", IncidentSeverity.Error, IncidentStatus.Open,
                new IncidentResolutionOutcome(IncidentResolutionActionKinds.WaitForIntervention, IncidentCreatedAt, null, IncidentResolutionSystemSources.StructuralIntervention, OfferMetadata()),
                "TestFailure", "The activity faulted.", IncidentCreatedAt, null)
        };
        var fixture = await Fixture.CreateAsync(incident);

        var result = await fixture.Handler.PreflightAsync(fixture.Context(Payload(), new Staging()));

        Assert.Equal("InterventionNotPending", result.Failure!.Code);
    }

    [Theory]
    [InlineData("test.other")]
    [InlineData("runtime.abandon")]
    [InlineData("not a code")]
    public async Task PreflightAsync_RejectsADecisionThatWasNotOffered(string decision)
    {
        var fixture = await Fixture.CreateAsync();

        var result = await fixture.Handler.PreflightAsync(fixture.Context(Payload(decision: decision), new Staging()));

        Assert.Equal("DecisionNotOffered", result.Failure!.Code);
    }

    [Fact]
    public async Task PreflightAsync_RejectsADecisionOnAWaitThatOfferedNone()
    {
        // A wait that started before spec 260 carries no offer: only a retry applies to it.
        var fixture = await Fixture.CreateAsync(WaitingIncident(offer: false));

        var result = await fixture.Handler.PreflightAsync(fixture.Context(Payload(), new Staging()));

        Assert.Equal("DecisionNotOffered", result.Failure!.Code);
    }

    [Fact]
    public async Task PreflightAsync_RejectsAMissingRequester()
    {
        var fixture = await Fixture.CreateAsync();

        var result = await fixture.Handler.PreflightAsync(fixture.Context(Payload(), new Staging(), activities: [Faulted()]));

        Assert.Equal("RequesterNotFound", result.Failure!.Code);
    }

    [Theory]
    [InlineData(ActivityExecutionStatus.Cancelled)]
    [InlineData(ActivityExecutionStatus.Completed)]
    [InlineData(ActivityExecutionStatus.Faulted)]
    public async Task PreflightAsync_RejectsARequesterThatIsNotRunning(ActivityExecutionStatus status)
    {
        var fixture = await Fixture.CreateAsync();

        var result = await fixture.Handler.PreflightAsync(fixture.Context(Payload(), new Staging(), activities: [Requester() with { Status = status }, Faulted()]));

        Assert.Equal("RequesterNotRunning", result.Failure!.Code);
    }

    [Fact]
    public async Task PreflightAsync_RejectsClaimedRequesterWork()
    {
        var fixture = await Fixture.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        var claim = new RuntimeSchedulerWorkClaim(
            new RuntimeSchedulerWorkItem("work", "workflow", "command", WorkflowExecutionCommandKind.CompleteActivity, "envelope", "key", now, now, executionScopeId: "requester-scope"),
            "worker", 1, 1, now, now.AddMinutes(1));

        var result = await fixture.Handler.PreflightAsync(fixture.Context(Payload(), new Staging(), claims: [claim]));

        Assert.Equal("RequesterWorkClaimed", result.Failure!.Code);
    }

    [Fact]
    public async Task PreflightAsync_RejectsAnUnavailablePinnedNode()
    {
        var fixture = await Fixture.CreateAsync(saveExecutable: false);

        var result = await fixture.Handler.PreflightAsync(fixture.Context(Payload(), new Staging()));

        Assert.Equal("PinnedExecutableUnavailable", result.Failure!.Code);
    }

    [Fact]
    public async Task PreflightAsync_ResolvesTheIncidentAndStagesOneDeliveryToTheRequester()
    {
        var fixture = await Fixture.CreateAsync();
        var staging = new Staging();

        var result = await fixture.Handler.PreflightAsync(fixture.Context(Payload(reason: "  Refunded manually, ticket FIN-221  "), staging));

        Assert.True(result.IsAccepted, result.Failure?.Message);
        var builder = Apply(Assert.Single(staging.StagedChanges));
        Assert.Empty(builder.ActivityExecutions);

        var incident = Assert.Single(builder.Incidents).State;
        Assert.Equal(IncidentStatus.Resolved, incident.Status);
        Assert.NotNull(incident.ResolvedAt);
        Assert.Equal(IncidentResolutionActionKinds.OperatorDecision, incident.ResolutionOutcome!.ActionKind);
        Assert.Equal(IncidentResolutionSystemSources.OperatorDecisionAlteration, incident.ResolutionOutcome.SystemSource);
        Assert.Equal(
            [
                new KeyValuePair<string, string>("runtime.alteration.jobId", "job"),
                new KeyValuePair<string, string>("runtime.alteration.planId", "plan"),
                new KeyValuePair<string, string>("runtime.alteration.reason", "Refunded manually, ticket FIN-221"),
                new KeyValuePair<string, string>("runtime.alteration.submittedBy", "operator-1"),
                new KeyValuePair<string, string>(RuntimeMetadataKeys.InterventionDecision, Resolve),
                new KeyValuePair<string, string>(RuntimeMetadataKeys.InterventionRequestedBy, "requester")
            ],
            incident.ResolutionOutcome.Metadata.OrderBy(item => item.Key, StringComparer.Ordinal).ToArray());
        Assert.Equal(IncidentResolutionActionKinds.WaitForIntervention, incident.Metadata[RuntimeMetadataKeys.PriorResolutionActionKind]);
        Assert.Equal(IncidentResolutionSystemSources.StructuralIntervention, incident.Metadata[RuntimeMetadataKeys.PriorResolutionSystemSource]);
        IncidentStateTransitionValidator.EnsureResolutionOutcomeIsWriteOnce(await fixture.Incidents.FindAsync("workflow", "incident"), incident);

        var intent = Assert.Single(builder.PostCommitIntents);
        Assert.Equal(RuntimePostCommitIntentKinds.EnqueueSchedulerWork, intent.Kind);
        var delivery = intent.MaterializedSchedulerWorkItem!;
        Assert.Equal(WorkflowExecutionCommandKind.DeliverInterventionDecision, delivery.CommandKind);
        Assert.Equal("requester-scope", delivery.ExecutionScopeId);
        Assert.Equal(DecideInterventionAlterationHandler.Kind, delivery.CommandMetadata["runtime.alteration.kind"]);
        var payload = delivery.Payload!.Value.Deserialize<RuntimeInterventionDecisionCommandPayload>()!;
        Assert.Equal("requester", payload.ActivityExecutionId);
        Assert.Equal("requester-node", payload.ExecutableNodeId);
        Assert.Equal("incident", payload.IncidentId);
        Assert.Equal(Resolve, payload.Decision);
        Assert.Equal("faulted", payload.FaultedActivityExecutionId);
        Assert.Equal("faulted-node", payload.FaultedExecutableNodeId);
        Assert.Empty(builder.ActivityScopeCleanups);
    }

    [Fact]
    public async Task PreflightAsync_ReplayStagesTheSameDelivery()
    {
        var fixture = await Fixture.CreateAsync();
        var first = new Staging();
        var replay = new Staging();

        Assert.True((await fixture.Handler.PreflightAsync(fixture.Context(Payload(decision: Abandon), first))).IsAccepted);
        Assert.True((await fixture.Handler.PreflightAsync(fixture.Context(Payload(decision: Abandon), replay))).IsAccepted);

        var firstIntent = Assert.Single(Apply(Assert.Single(first.StagedChanges)).PostCommitIntents);
        var replayIntent = Assert.Single(Apply(Assert.Single(replay.StagedChanges)).PostCommitIntents);
        Assert.Equal(firstIntent.IntentId, replayIntent.IntentId);
        Assert.Equal(firstIntent.MaterializedSchedulerWorkItem!.WorkItemId, replayIntent.MaterializedSchedulerWorkItem!.WorkItemId);
        Assert.Equal(firstIntent.IdempotencyKey, replayIntent.IdempotencyKey);
        Assert.Equal(Assert.Single(first.StagedChanges).ChangeKey, Assert.Single(replay.StagedChanges).ChangeKey);
    }

    [Fact]
    public void Registry_ReservesTheKindForTheRuntime()
    {
        Assert.Throws<ArgumentException>(() => WorkflowAlterationRegistry.ValidateCustomDescriptor(
            new WorkflowAlterationDescriptor(DecideInterventionAlterationHandler.Kind, 1, "Spoofed decision")));
    }

    private sealed class Fixture
    {
        private Fixture(DecideInterventionAlterationHandler handler, InMemoryIncidentStateStore incidents)
        {
            Handler = handler;
            Incidents = incidents;
        }

        public DecideInterventionAlterationHandler Handler { get; }
        public InMemoryIncidentStateStore Incidents { get; }

        public static async Task<Fixture> CreateAsync(IncidentState? incident = null, bool saveExecutable = true)
        {
            var executables = new InMemoryWorkflowExecutableStore();
            if (saveExecutable)
                await executables.SaveAsync(Executable());
            var incidents = new InMemoryIncidentStateStore();
            var alterations = new InMemoryWorkflowAlterationStore();
            await alterations.AdmitAsync(WorkflowAlterationPlanState.CreateCapturing(
                "plan",
                new WorkflowAlterationAuthorityScope("tenant", "system", "operator"),
                new WorkflowAlterationOperatorProvenance("operator-1", null),
                "key-hash",
                "request-hash",
                new ProtectedWorkflowAlterationPayload("key-1", "AES-256-GCM", "ciphertext"),
                WorkflowAlterationTargetSelector.ForExecutionIds(["workflow"]),
                DateTimeOffset.UnixEpoch));
            await incidents.SaveAsync(incident ?? WaitingIncident());
            return new Fixture(new DecideInterventionAlterationHandler(executables, incidents, alterations, TimeProvider.System), incidents);
        }

        public WorkflowAlterationPreflightContext Context(
            JsonElement payload,
            Staging staging,
            WorkflowExecutionState? workflow = null,
            WorkflowAlterationJobState? job = null,
            IReadOnlyCollection<ActivityExecutionState>? activities = null,
            IReadOnlyCollection<RuntimeSchedulerWorkClaim>? claims = null) => new(
            job ?? Job(),
            new WorkflowAlterationEnvelope(DecideInterventionAlterationHandler.Kind, 1, payload),
            0,
            new WorkflowAlterationProjectedState(
            [
                workflow ?? Workflow(),
                new WorkflowAlterationActorCommandExecutor.WorkflowAlterationActivityExecutionProjection(activities ?? [Requester(), Faulted()], claims)
            ]),
            staging);
    }

    private static Dictionary<string, string> OfferMetadata() => new()
    {
        [RuntimeMetadataKeys.InterventionDecisions] = JsonSerializer.Serialize(new[] { Resolve, Abandon }),
        [RuntimeMetadataKeys.InterventionRequestedBy] = "requester"
    };

    private static IncidentState WaitingIncident(string incidentId = "incident", string workflowExecutionId = "workflow", bool offer = true) => new(
        incidentId, workflowExecutionId, "faulted", "faulted-node", IncidentSeverity.Error, IncidentStatus.Blocking,
        new IncidentResolutionOutcome(
            IncidentResolutionActionKinds.WaitForIntervention,
            IncidentCreatedAt,
            null,
            IncidentResolutionSystemSources.StructuralIntervention,
            offer ? OfferMetadata() : new Dictionary<string, string>()),
        "TestFailure", "The activity faulted.", IncidentCreatedAt, null,
        new Dictionary<string, string> { [RuntimeMetadataKeys.InterventionRequestedBy] = "requester" });

    private static JsonElement Payload(string incidentId = "incident", string decision = Resolve, string reason = "Operator reason") =>
        JsonSerializer.SerializeToElement(new { incidentId, decision, reason });

    private static WorkflowAlterationRuntimeCheckpointStateBuilder Apply(IWorkflowAlterationStagedChange change)
    {
        var builder = new WorkflowAlterationRuntimeCheckpointStateBuilder(Workflow());
        Assert.IsAssignableFrom<IWorkflowAlterationRuntimeCheckpointStagedChange>(change).Apply(builder);
        return builder;
    }

    private static WorkflowAlterationJobState Job() => new("job", "plan", "workflow", "tenant", 0, WorkflowAlterationJobStatus.Running,
        new WorkflowAlterationJobClaim("worker", "claim", DateTimeOffset.MaxValue), 1, [], null, null, DateTimeOffset.UnixEpoch, null, null, 0);

    private static WorkflowExecutionState Workflow(WorkflowExecutionStatus status = WorkflowExecutionStatus.Running) =>
        new("workflow", new WorkflowExecutableIdentity("artifact", "definition", "version", "1", "hash"), status,
            null, DateTimeOffset.UnixEpoch, null, null, null, null, null, "tenant", new Dictionary<string, string>());

    private static WorkflowExecutable Executable()
    {
        var node = new ExecutableNode("requester-node", "requester-node", "test", "1", "consumer", JsonSerializer.SerializeToElement(new { }),
            new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>());
        return new WorkflowExecutable(Workflow().PinnedExecutable, node, new Dictionary<string, WorkflowExecutableResumeTarget>(),
            DateTimeOffset.UnixEpoch, new Dictionary<string, string>(), IncidentStrategyBuiltIns.FaultReference);
    }

    private static ActivityExecutionState Requester() => Activity("requester", "requester-node", ActivityExecutionStatus.Running, parent: null) with
    {
        ExecutionScopeId = "requester-scope"
    };

    private static ActivityExecutionState Faulted() => Activity("faulted", "faulted-node", ActivityExecutionStatus.Faulted, parent: "requester");

    private static ActivityExecutionState Activity(string id, string node, ActivityExecutionStatus status, string? parent) => new(
        new ActivityExecution(id, "workflow", node, node, "test", "1"),
        status, null, 1, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, null,
        parent, parent, null, null,
        ActivitySchedulingProvenance.From("workflow", parent, parent, null, null, null, id, "test", new Dictionary<string, string>()),
        null, [], [], 0, 0, new Dictionary<string, string>());

    private sealed class Staging : IWorkflowAlterationStagingWorkspace
    {
        public List<IWorkflowAlterationStagedChange> StagedChanges { get; } = [];
        IReadOnlyList<IWorkflowAlterationStagedChange> IWorkflowAlterationStagingWorkspace.StagedChanges => StagedChanges;
        public void Stage(IWorkflowAlterationStagedChange change) => StagedChanges.Add(change);
    }
}
