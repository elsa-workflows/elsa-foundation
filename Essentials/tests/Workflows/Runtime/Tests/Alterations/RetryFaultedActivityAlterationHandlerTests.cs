using System.Text.Json;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;
using Elsa.Workflows.Runtime.Services.Alterations;
using Elsa.Workflows.Runtime.Services.Alterations.Handlers;
using Elsa.Workflows.Runtime.Services.Bookmarks;
using Elsa.Workflows.Runtime.Services.Executables;
using Elsa.Workflows.Runtime.Services.Incidents;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests.Alterations;

public sealed class RetryFaultedActivityAlterationHandlerTests
{
    private static readonly DateTimeOffset IncidentCreatedAt = DateTimeOffset.UnixEpoch;

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"sourceActivityExecutionId":" "}""")]
    [InlineData("""{"sourceActivityExecutionId":"source","extra":true}""")]
    [InlineData("""{"sourceActivityExecutionId":"source","reason":42}""")]
    [InlineData("""{"sourceActivityExecutionId":"source","reason":"line\nbreak"}""")]
    public async Task PreflightAsync_RejectsMalformedPayloads(string payload)
    {
        var fixture = await Fixture.CreateAsync();
        var staging = new Staging();

        var result = await fixture.Handler.PreflightAsync(Context(JsonDocument.Parse(payload).RootElement.Clone(), staging, fixture.Source));

        Assert.Equal("InvalidRetryFaultedActivityPayload", result.Failure!.Code);
        Assert.Empty(staging.StagedChanges);
    }

    [Fact]
    public async Task PreflightAsync_RejectsAReasonLongerThanTheLimit()
    {
        var fixture = await Fixture.CreateAsync();

        var result = await fixture.Handler.PreflightAsync(Context(Payload("source", new string('r', RetryFaultedActivityAlterationHandler.MaxReasonLength + 1)), new Staging(), fixture.Source));

        Assert.Equal("InvalidRetryFaultedActivityPayload", result.Failure!.Code);
    }

    [Fact]
    public async Task PreflightAsync_RejectsATerminalWorkflow()
    {
        var fixture = await Fixture.CreateAsync();

        var result = await fixture.Handler.PreflightAsync(Context(Payload("source"), new Staging(), fixture.Source, workflow: Workflow(WorkflowExecutionStatus.Faulted)));

        Assert.Equal("WorkflowTerminal", result.Failure!.Code);
    }

    [Fact]
    public async Task PreflightAsync_RejectsAMissingSource()
    {
        var fixture = await Fixture.CreateAsync();

        var result = await fixture.Handler.PreflightAsync(Context(Payload("missing"), new Staging(), fixture.Source));

        Assert.Equal("SourceActivityNotFound", result.Failure!.Code);
    }

    [Theory]
    [InlineData(ActivityExecutionStatus.Scheduled)]
    [InlineData(ActivityExecutionStatus.Suspended)]
    [InlineData(ActivityExecutionStatus.Completed)]
    [InlineData(ActivityExecutionStatus.Superseded)]
    public async Task PreflightAsync_RejectsASourceThatIsNotFaulted(ActivityExecutionStatus status)
    {
        var fixture = await Fixture.CreateAsync();

        var result = await fixture.Handler.PreflightAsync(Context(Payload("source"), new Staging(), fixture.Source with { Status = status }));

        Assert.Equal("SourceActivityNotFaulted", result.Failure!.Code);
    }

    [Theory]
    [InlineData(ActivityExecutionStatus.Cancelled)]
    [InlineData(ActivityExecutionStatus.Completed)]
    [InlineData(ActivityExecutionStatus.Faulted)]
    public async Task PreflightAsync_RejectsASourceWhoseParentIsNotRunning(ActivityExecutionStatus parentStatus)
    {
        // Spec 260 R9: a retry under a cancelled scope would start its handler while nothing receives the outcome.
        var fixture = await Fixture.CreateAsync();
        var staging = new Staging();
        var parent = Source("parent") with { Status = parentStatus, ParentActivityExecutionId = null, IncidentIds = [] };

        var result = await fixture.Handler.PreflightAsync(Context(Payload("source"), staging, [fixture.Source, parent], []));

        Assert.Equal("SourceParentNotRunning", result.Failure!.Code);
        Assert.Empty(staging.StagedChanges);
    }

    [Fact]
    public async Task PreflightAsync_AcceptsASourceWhoseParentIsRunning()
    {
        var fixture = await Fixture.CreateAsync();
        var parent = Source("parent") with { Status = ActivityExecutionStatus.Running, ParentActivityExecutionId = null, IncidentIds = [] };

        var result = await fixture.Handler.PreflightAsync(Context(Payload("source"), new Staging(), [fixture.Source, parent], []));

        Assert.True(result.IsAccepted, result.Failure?.Message);
    }

    [Fact]
    public async Task PreflightAsync_RejectsASourceWithoutABlockingIncident()
    {
        var fixture = await Fixture.CreateAsync(incident: null);

        var result = await fixture.Handler.PreflightAsync(Context(Payload("source"), new Staging(), fixture.Source));

        Assert.Equal("NoBlockingIncident", result.Failure!.Code);
    }

    [Fact]
    public async Task PreflightAsync_RejectsASourceWhoseIncidentWasAlreadyResolved()
    {
        var resolved = new IncidentState(
            "incident", "workflow", "source", "node", IncidentSeverity.Error, IncidentStatus.Resolved,
            new IncidentResolutionOutcome(IncidentResolutionActionKinds.AbsorbFault, IncidentCreatedAt, null, IncidentResolutionSystemSources.StructuralFaultAbsorption),
            "TestFailure", "The activity faulted.", IncidentCreatedAt, IncidentCreatedAt);
        var fixture = await Fixture.CreateAsync(resolved);

        var result = await fixture.Handler.PreflightAsync(Context(Payload("source"), new Staging(), fixture.Source));

        Assert.Equal("NoBlockingIncident", result.Failure!.Code);
    }

    [Fact]
    public async Task PreflightAsync_RejectsABlockingIncidentOnADescendant()
    {
        var fixture = await Fixture.CreateAsync();
        await fixture.Incidents.SaveAsync(WaitingIncident("descendant-incident", "descendant"));
        var descendant = Source("descendant") with { ParentActivityExecutionId = "source" };

        var result = await fixture.Handler.PreflightAsync(Context(Payload("source"), new Staging(), fixture.Source, descendant));

        Assert.Equal("BlockingIncidentOutsideSource", result.Failure!.Code);
    }

    [Fact]
    public async Task PreflightAsync_RejectsAnIncidentStillAwaitingItsParentOrStrategy()
    {
        var fixture = await Fixture.CreateAsync(new IncidentState(
            "incident", "workflow", "source", "node", IncidentSeverity.Error, IncidentStatus.Blocking, null,
            "TestFailure", "The activity faulted.", IncidentCreatedAt, null));

        var result = await fixture.Handler.PreflightAsync(Context(Payload("source"), new Staging(), fixture.Source));

        Assert.Equal("IncidentNotAwaitingIntervention", result.Failure!.Code);
    }

    [Fact]
    public async Task PreflightAsync_RejectsClaimedSourceWork()
    {
        var fixture = await Fixture.CreateAsync();
        var now = DateTimeOffset.UtcNow;
        var claim = new RuntimeSchedulerWorkClaim(
            new RuntimeSchedulerWorkItem("work", "workflow", "command", WorkflowExecutionCommandKind.InvokeActivity, "envelope", "key", now, now, executionScopeId: "source"),
            "worker", 1, 1, now, now.AddMinutes(1));

        var result = await fixture.Handler.PreflightAsync(Context(Payload("source"), new Staging(), [fixture.Source], [claim]));

        Assert.Equal("SourceWorkClaimed", result.Failure!.Code);
    }

    [Fact]
    public async Task PreflightAsync_RejectsAnUnavailablePinnedNode()
    {
        var fixture = await Fixture.CreateAsync(WaitingIncident("incident", "source"), saveExecutable: false);

        var result = await fixture.Handler.PreflightAsync(Context(Payload("source"), new Staging(), fixture.Source));

        Assert.Equal("PinnedExecutableUnavailable", result.Failure!.Code);
    }

    [Fact]
    public async Task PreflightAsync_RejectsASourceChangedAfterCapture()
    {
        var fixture = await Fixture.CreateAsync();
        var job = new WorkflowAlterationJobState("job", "plan", "workflow", "tenant", 0, WorkflowAlterationJobStatus.Running,
            new WorkflowAlterationJobClaim("worker", "claim", DateTimeOffset.MaxValue), 1, [], null, null, DateTimeOffset.UnixEpoch, null, null, 0,
            new WorkflowAlterationCapturedConcurrency(null, null, Workflow().PinnedExecutable, new Dictionary<string, string> { ["source"] = "stale" }));

        var result = await fixture.Handler.PreflightAsync(new WorkflowAlterationPreflightContext(
            job,
            new WorkflowAlterationEnvelope(RetryFaultedActivityAlterationHandler.Kind, 1, Payload("source")),
            0,
            new WorkflowAlterationProjectedState([Workflow(), new WorkflowAlterationActorCommandExecutor.WorkflowAlterationActivityExecutionProjection([fixture.Source])]),
            new Staging()));

        Assert.Equal("ActivityStateRevisionConflict", result.Failure!.Code);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreflightAsync_ResolvesTheIncidentAndStagesASuccessorKeepingTheSourcesIdentity(bool structureSnapshot)
    {
        var fixture = await Fixture.CreateAsync();
        var source = structureSnapshot ? fixture.Source with
        {
            InputSnapshot = new ActivityInputSnapshot("source", "contract", "binding", new Dictionary<string, ValueEnvelope>
            { ["Case:first:Condition"] = ValueEnvelope.Inline(new ValueTypeDescriptor("Boolean"), JsonSerializer.SerializeToElement(true), ValueProtectionPolicy.InstanceInline) }, DateTimeOffset.UnixEpoch)
        } : fixture.Source;
        var staging = new Staging();

        var result = await fixture.Handler.PreflightAsync(Context(Payload("source", "  Delivery service restored  "), staging, source));

        Assert.True(result.IsAccepted, result.Failure?.Message);
        var builder = Apply(Assert.Single(staging.StagedChanges));
        var superseded = Assert.Single(builder.ActivityExecutions, change => change.StateId == "source").State;
        var successor = Assert.Single(builder.ActivityExecutions, change => change.StateId != "source").State;
        Assert.Equal(ActivityExecutionStatus.Superseded, superseded.Status);
        Assert.Equal(successor.Execution.ActivityExecutionId, superseded.SupersededByActivityExecutionId);

        Assert.Equal(ActivityExecutionStatus.Running, successor.Status);
        Assert.Same(source.InputSnapshot, successor.InputSnapshot);
        if (structureSnapshot)
        {
            Assert.True(successor.InputSnapshot!.Values["Case:first:Condition"].InlineValue!.Value.GetBoolean());
            Assert.False(successor.InputSnapshot.Values.ContainsKey("Case:later:Condition"));
        }
        Assert.Equal("parent", successor.ParentActivityExecutionId);
        Assert.Equal("parent", successor.SchedulingActivityExecutionId);
        Assert.Equal("compensation", successor.Provenance.SchedulingCause);
        Assert.Equal("entry-1", successor.Provenance.Metadata["compensation.entryId"]);
        Assert.Equal("first-attempt", successor.Provenance.Metadata[RuntimeMetadataKeys.OperationKey]);
        Assert.False(successor.Provenance.Metadata.ContainsKey(RuntimeMetadataKeys.TargetOperationKey));
        Assert.Equal("first-attempt", ActivityOperationKeyResolver.ResolveOperationKey(successor));
        Assert.Equal("source", successor.Metadata[RuntimeMetadataKeys.RetryOfActivityExecutionId]);
        Assert.Equal("source", successor.Metadata[RuntimeMetadataKeys.SupersedesActivityExecutionId]);
        Assert.Equal("kept", successor.Metadata["custom.flag"]);
        Assert.False(successor.Metadata.ContainsKey(RuntimeMetadataKeys.ParentCompletionProcessedWorkItemId));
        Assert.False(successor.Metadata.ContainsKey(RuntimeMetadataKeys.IncidentId));
        Assert.False(successor.Metadata.ContainsKey(RuntimeMetadataKeys.FaultMessage));
        Assert.Empty(successor.IncidentIds);
        Assert.Null(successor.Fault);

        var incident = Assert.Single(builder.Incidents).State;
        Assert.Equal(IncidentStatus.Resolved, incident.Status);
        Assert.NotNull(incident.ResolvedAt);
        Assert.Equal(IncidentResolutionActionKinds.OperatorRetry, incident.ResolutionOutcome!.ActionKind);
        Assert.Equal(IncidentResolutionSystemSources.OperatorRetryAlteration, incident.ResolutionOutcome.SystemSource);
        Assert.Equal(
            [
                new KeyValuePair<string, string>("runtime.alteration.jobId", "job"),
                new KeyValuePair<string, string>("runtime.alteration.planId", "plan"),
                new KeyValuePair<string, string>("runtime.alteration.reason", "Delivery service restored"),
                new KeyValuePair<string, string>("runtime.alteration.submittedBy", "operator-1")
            ],
            incident.ResolutionOutcome.Metadata.OrderBy(item => item.Key, StringComparer.Ordinal).ToArray());
        Assert.Equal(IncidentResolutionActionKinds.WaitForIntervention, incident.Metadata[RuntimeMetadataKeys.PriorResolutionActionKind]);
        Assert.Equal(IncidentResolutionSystemSources.StructuralIntervention, incident.Metadata[RuntimeMetadataKeys.PriorResolutionSystemSource]);
        IncidentStateTransitionValidator.EnsureResolutionOutcomeIsWriteOnce(await fixture.Incidents.FindAsync("workflow", "incident"), incident);

        var continuation = Assert.Single(builder.PostCommitIntents).MaterializedSchedulerWorkItem!;
        Assert.Equal(WorkflowExecutionCommandKind.StartActivity, continuation.CommandKind);
        Assert.Equal(RetryFaultedActivityAlterationHandler.Kind, continuation.CommandMetadata["runtime.alteration.kind"]);
        Assert.Equal("source", Assert.Single(builder.ActivityScopeCleanups).ExecutionScopeId);
    }

    [Fact]
    public async Task PreflightAsync_KeepsAnAssignedTargetOperationKey()
    {
        var fixture = await Fixture.CreateAsync();
        var source = fixture.Source with
        {
            Provenance = fixture.Source.Provenance with
            {
                Metadata = new Dictionary<string, string>
                {
                    [RuntimeMetadataKeys.OperationKey] = "scope/step/0/compensate",
                    [RuntimeMetadataKeys.TargetOperationKey] = "scope/step/0"
                }
            }
        };
        var staging = new Staging();

        Assert.True((await fixture.Handler.PreflightAsync(Context(Payload("source"), staging, source))).IsAccepted);

        var successor = Assert.Single(Apply(Assert.Single(staging.StagedChanges)).ActivityExecutions, change => change.StateId != "source").State;
        Assert.Equal("scope/step/0/compensate", ActivityOperationKeyResolver.ResolveOperationKey(successor));
        Assert.Equal("scope/step/0", ActivityOperationKeyResolver.ResolveTargetOperationKey(successor));
    }

    [Fact]
    public async Task PreflightAsync_ReplayDerivesTheSameSuccessorAndContinuation()
    {
        var fixture = await Fixture.CreateAsync();
        var first = new Staging();
        var replay = new Staging();

        Assert.True((await fixture.Handler.PreflightAsync(Context(Payload("source"), first, fixture.Source))).IsAccepted);
        Assert.True((await fixture.Handler.PreflightAsync(Context(Payload("source"), replay, fixture.Source))).IsAccepted);

        var firstBuilder = Apply(Assert.Single(first.StagedChanges));
        var replayBuilder = Apply(Assert.Single(replay.StagedChanges));
        Assert.Equal(
            Assert.Single(firstBuilder.ActivityExecutions, change => change.StateId != "source").StateId,
            Assert.Single(replayBuilder.ActivityExecutions, change => change.StateId != "source").StateId);
        Assert.Equal(Assert.Single(firstBuilder.PostCommitIntents).IntentId, Assert.Single(replayBuilder.PostCommitIntents).IntentId);
        Assert.Equal(Assert.Single(first.StagedChanges).ChangeKey, Assert.Single(replay.StagedChanges).ChangeKey);
    }

    private sealed class Fixture
    {
        private Fixture(RetryFaultedActivityAlterationHandler handler, InMemoryIncidentStateStore incidents, ActivityExecutionState source)
        {
            Handler = handler;
            Incidents = incidents;
            Source = source;
        }

        public RetryFaultedActivityAlterationHandler Handler { get; }
        public InMemoryIncidentStateStore Incidents { get; }
        public ActivityExecutionState Source { get; }

        public static async Task<Fixture> CreateAsync(IncidentState? incident, bool saveExecutable = true)
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
            var handler = new RetryFaultedActivityAlterationHandler(
                executables,
                incidents,
                alterations,
                new ActivityExecutionSupersessionPlanner(new ActivityScopeCleanupStore(new InMemoryBookmarkStateStore(), new InMemoryDurableTimerStore(), new InMemoryWorkflowSchedulerWorkQueue())),
                TimeProvider.System);
            if (incident is not null)
                await incidents.SaveAsync(incident);
            return new Fixture(handler, incidents, Source("source"));
        }

        public static Task<Fixture> CreateAsync() => CreateAsync(WaitingIncident("incident", "source"));
    }

    private static IncidentState WaitingIncident(string incidentId, string activityExecutionId) => new(
        incidentId, "workflow", activityExecutionId, "node", IncidentSeverity.Error, IncidentStatus.Blocking,
        new IncidentResolutionOutcome(
            IncidentResolutionActionKinds.WaitForIntervention,
            IncidentCreatedAt,
            null,
            IncidentResolutionSystemSources.StructuralIntervention,
            new Dictionary<string, string> { ["compensation.entryId"] = "entry-1" }),
        "TestFailure", "The activity faulted.", IncidentCreatedAt, null);

    private static JsonElement Payload(string sourceId, string? reason = null) => reason is null
        ? JsonSerializer.SerializeToElement(new { sourceActivityExecutionId = sourceId })
        : JsonSerializer.SerializeToElement(new { sourceActivityExecutionId = sourceId, reason });

    private static WorkflowAlterationPreflightContext Context(
        JsonElement payload,
        Staging staging,
        ActivityExecutionState source,
        params ActivityExecutionState[] descendants) =>
        Context(payload, staging, [source, .. descendants], []);

    private static WorkflowAlterationPreflightContext Context(
        JsonElement payload,
        Staging staging,
        ActivityExecutionState source,
        WorkflowExecutionState workflow) => new(
        Job(),
        new WorkflowAlterationEnvelope(RetryFaultedActivityAlterationHandler.Kind, 1, payload),
        0,
        new WorkflowAlterationProjectedState([workflow, new WorkflowAlterationActorCommandExecutor.WorkflowAlterationActivityExecutionProjection([source])]),
        staging);

    private static WorkflowAlterationPreflightContext Context(
        JsonElement payload,
        Staging staging,
        IReadOnlyCollection<ActivityExecutionState> activities,
        IReadOnlyCollection<RuntimeSchedulerWorkClaim> claims) => new(
        Job(),
        new WorkflowAlterationEnvelope(RetryFaultedActivityAlterationHandler.Kind, 1, payload),
        0,
        new WorkflowAlterationProjectedState([Workflow(), new WorkflowAlterationActorCommandExecutor.WorkflowAlterationActivityExecutionProjection(activities, claims)]),
        staging);

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
        var node = new ExecutableNode("node", "node", "test", "1", "consumer", JsonSerializer.SerializeToElement(new { }),
            new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>());
        return new WorkflowExecutable(Workflow().PinnedExecutable, node, new Dictionary<string, WorkflowExecutableResumeTarget>(),
            DateTimeOffset.UnixEpoch, new Dictionary<string, string>(), IncidentStrategyBuiltIns.FaultReference);
    }

    private static ActivityExecutionState Source(string id) => new(
        new ActivityExecution(id, "workflow", "node", "node", "test", "1"),
        ActivityExecutionStatus.Faulted, "ActivityReturnedFault", 1, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch,
        "parent", "parent", null, null,
        ActivitySchedulingProvenance.From("workflow", "parent", "parent", null, null, null, id, "compensation",
            new Dictionary<string, string> { ["compensation.entryId"] = "entry-1" }),
        null, [], ["incident"], 1, 1,
        new Dictionary<string, string>
        {
            ["custom.flag"] = "kept",
            [RuntimeMetadataKeys.ParentCompletionProcessedWorkItemId] = "parent-evaluation",
            [RuntimeMetadataKeys.IncidentId] = "incident",
            [RuntimeMetadataKeys.FaultMessage] = "The activity faulted."
        })
    {
        ExecutionScopeId = id,
        InputSnapshot = new ActivityInputSnapshot(id, "contract", "binding", new Dictionary<string, ValueEnvelope>(), DateTimeOffset.UnixEpoch),
        Attempt = new ActivityExecutionAttemptLineage(1, "first-attempt", null),
        Fault = new NormalizedActivityFault("TEST", "Test.Exception", "The activity faulted.", null, false)
    };

    private sealed class Staging : IWorkflowAlterationStagingWorkspace
    {
        public List<IWorkflowAlterationStagedChange> StagedChanges { get; } = [];
        IReadOnlyList<IWorkflowAlterationStagedChange> IWorkflowAlterationStagingWorkspace.StagedChanges => StagedChanges;
        public void Stage(IWorkflowAlterationStagedChange change) => StagedChanges.Add(change);
    }
}
