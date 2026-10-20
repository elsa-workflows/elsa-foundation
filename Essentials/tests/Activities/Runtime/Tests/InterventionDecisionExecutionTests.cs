using System.Text.Json;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Runtime.Services;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Elsa.Workflows.Runtime.Services.Alterations;
using Elsa.Workflows.Runtime.Services.Alterations.Handlers;
using Elsa.Workflows.Runtime.Services.Recovery;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// Runtime seam R-5 (spec 260) end to end with a test structural activity, not the compensation scope: a parent offers
/// decisions when it requests intervention, <c>DecideIntervention/1</c> resolves the incident and enqueues the decision,
/// and the parent applies it in its own evaluation. Covers the offer, each continuation, the acks (a requester that is
/// no longer running, a repeated item), misuse, idempotency and the stale refusals.
/// </summary>
public sealed class InterventionDecisionExecutionTests
{
    private const string Continue = DecisionParentActivity.Continue;
    private const string Complete = DecisionParentActivity.Complete;
    private const string Fault = DecisionParentActivity.Fault;
    private static readonly string[] Offer = [Continue, Complete, Fault, DecisionParentActivity.Absorb, DecisionParentActivity.Intervene, DecisionParentActivity.CancelAndFault];

    [Fact]
    public async Task An_offer_rides_the_wait_outcome_with_the_requester()
    {
        await using var fixture = await DecisionFixture.StartAsync(new DecisionDirective(Offer));

        Assert.Equal(WorkflowExecutionStatus.Running, fixture.Run.WorkflowState!.Status);
        var incident = Assert.Single(await fixture.ListIncidentsAsync());
        Assert.Equal(IncidentStatus.Blocking, incident.Status);
        var outcome = incident.ResolutionOutcome!;
        Assert.Equal(IncidentResolutionActionKinds.WaitForIntervention, outcome.ActionKind);
        Assert.Equal(Offer, JsonSerializer.Deserialize<string[]>(outcome.Metadata[RuntimeMetadataKeys.InterventionDecisions]));
        Assert.Equal("actexec-parent", outcome.Metadata[RuntimeMetadataKeys.InterventionRequestedBy]);
        Assert.Equal("scope-1", outcome.Metadata["test.scope"]);
    }

    [Fact]
    public async Task A_request_without_an_offer_writes_neither_key()
    {
        await using var fixture = await DecisionFixture.StartAsync(new DecisionDirective(Offer: null));

        var outcome = Assert.Single(await fixture.ListIncidentsAsync()).ResolutionOutcome!;
        Assert.False(outcome.Metadata.ContainsKey(RuntimeMetadataKeys.InterventionDecisions));
        Assert.False(outcome.Metadata.ContainsKey(RuntimeMetadataKeys.InterventionRequestedBy));
    }

    [Fact]
    public async Task Request_metadata_cannot_forge_the_offer_or_the_requester()
    {
        await using (var unoffered = await DecisionFixture.StartAsync(new DecisionDirective(Offer: null) { ForgesOfferMetadata = true }))
        {
            var outcome = Assert.Single(await unoffered.ListIncidentsAsync()).ResolutionOutcome!;
            Assert.False(outcome.Metadata.ContainsKey(RuntimeMetadataKeys.InterventionDecisions));
            Assert.False(outcome.Metadata.ContainsKey(RuntimeMetadataKeys.InterventionRequestedBy));
            Assert.Equal("scope-1", outcome.Metadata["test.scope"]);
        }

        await using var offered = await DecisionFixture.StartAsync(new DecisionDirective(Offer) { ForgesOfferMetadata = true });
        var offeredOutcome = Assert.Single(await offered.ListIncidentsAsync()).ResolutionOutcome!;
        Assert.Equal(Offer, JsonSerializer.Deserialize<string[]>(offeredOutcome.Metadata[RuntimeMetadataKeys.InterventionDecisions]));
        Assert.Equal("actexec-parent", offeredOutcome.Metadata[RuntimeMetadataKeys.InterventionRequestedBy]);
    }

    public static TheoryData<string[]> InvalidOffers() =>
    [
        [],
        ["a.b", "a.c", "a.d", "a.e", "a.f", "a.g", "a.h", "a.i", "a.j"],
        ["nodot"],
        ["Upper.case"],
        ["runtime.retry"],
        ["test." + new string('x', 70)],
        ["test.same", "test.same"]
    ];

    [Theory]
    [MemberData(nameof(InvalidOffers))]
    public async Task An_invalid_offer_faults_the_evaluation(string[] offer)
    {
        await using var fixture = await DecisionFixture.StartAsync(new DecisionDirective(offer));

        var parent = fixture.Run.State("node-parent");
        Assert.Equal(ActivityExecutionStatus.Faulted, parent.Status);
        Assert.Contains("decision", parent.Fault!.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(await fixture.ListIncidentsAsync(), incident =>
            incident.ResolutionOutcome?.ActionKind == IncidentResolutionActionKinds.WaitForIntervention);
    }

    [Fact]
    public async Task An_offer_from_a_parent_without_the_callback_faults_the_evaluation()
    {
        await using var fixture = await DecisionFixture.StartAsync(new DecisionDirective(Offer), parentType: typeof(OfferWithoutCallbackParentActivity));

        var parent = fixture.Run.State("node-parent");
        Assert.Equal(ActivityExecutionStatus.Faulted, parent.Status);
        Assert.Contains(nameof(IRuntimeActivityInterventionDecisionHandler), parent.Fault!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_decision_to_continue_resolves_the_incident_and_the_requester_schedules_its_next_child()
    {
        await using var fixture = await DecisionFixture.StartAsync(new DecisionDirective(Offer));
        var incidentId = Assert.Single(await fixture.ListIncidentsAsync()).IncidentId;

        var job = await fixture.DecideAsync(incidentId, Continue, "decide-continue", "Effect absent, ticket FIN-221");

        Assert.Equal(WorkflowAlterationJobStatus.Succeeded, job.Status);
        var run = await fixture.ReadRunAsync();
        run.AssertWorkflowCompleted();
        run.AssertCompleted("node-parent");
        run.AssertCompleted("node-next");
        Assert.Equal(ActivityExecutionStatus.Faulted, run.State("node-f").Status);

        var decided = Assert.Single(fixture.Log.Decisions);
        Assert.Equal(incidentId, decided.IncidentId);
        Assert.Equal(Continue, decided.Decision);
        Assert.Equal(run.State("node-f").Execution.ActivityExecutionId, decided.FaultedActivityExecutionId);
        Assert.Equal("node-f", decided.FaultedExecutableNodeId);
        Assert.Equal(decided.FaultedActivityExecutionId, decided.ViaChildActivityExecutionId);
        Assert.Equal("node-f", decided.ViaChildExecutableNodeId);

        var incident = Assert.Single(await fixture.ListIncidentsAsync());
        Assert.Equal(IncidentStatus.Resolved, incident.Status);
        var outcome = incident.ResolutionOutcome!;
        Assert.Equal(IncidentResolutionActionKinds.OperatorDecision, outcome.ActionKind);
        Assert.Equal(IncidentResolutionSystemSources.OperatorDecisionAlteration, outcome.SystemSource);
        Assert.Equal(job.JobId, outcome.Metadata["runtime.alteration.jobId"]);
        Assert.Equal(job.PlanId, outcome.Metadata["runtime.alteration.planId"]);
        Assert.Equal(DecisionFixture.Operator, outcome.Metadata["runtime.alteration.submittedBy"]);
        Assert.Equal("Effect absent, ticket FIN-221", outcome.Metadata["runtime.alteration.reason"]);
        Assert.Equal(Continue, outcome.Metadata[RuntimeMetadataKeys.InterventionDecision]);
        Assert.Equal("actexec-parent", outcome.Metadata[RuntimeMetadataKeys.InterventionRequestedBy]);
        Assert.Equal(IncidentResolutionActionKinds.WaitForIntervention, incident.Metadata[RuntimeMetadataKeys.PriorResolutionActionKind]);
    }

    [Fact]
    public async Task A_decision_to_complete_completes_the_requester_and_records_the_processed_marker()
    {
        await using var fixture = await DecisionFixture.StartAsync(new DecisionDirective(Offer));
        var incidentId = Assert.Single(await fixture.ListIncidentsAsync()).IncidentId;

        Assert.Equal(WorkflowAlterationJobStatus.Succeeded, (await fixture.DecideAsync(incidentId, Complete, "decide-complete")).Status);

        var run = await fixture.ReadRunAsync();
        run.AssertWorkflowCompleted();
        run.AssertCompleted("node-parent");
        run.AssertDidNotRun("node-next");
        Assert.True(run.State("node-parent").Metadata.ContainsKey(RuntimeMetadataKeys.InterventionDecisionProcessedWorkItemId));
    }

    [Fact]
    public async Task A_decision_to_fault_faults_the_requester_with_its_fault()
    {
        await using var fixture = await DecisionFixture.StartAsync(new DecisionDirective(Offer));
        var incidentId = Assert.Single(await fixture.ListIncidentsAsync()).IncidentId;

        Assert.Equal(WorkflowAlterationJobStatus.Succeeded, (await fixture.DecideAsync(incidentId, Fault, "decide-fault")).Status);

        var run = await fixture.ReadRunAsync();
        var parent = run.State("node-parent");
        Assert.Equal(ActivityExecutionStatus.Faulted, parent.Status);
        Assert.Equal(DecisionParentActivity.DecidedFaultCode, parent.Fault!.Code);
        Assert.Equal(WorkflowExecutionStatus.Faulted, run.WorkflowState!.Status);
    }

    [Theory]
    [InlineData(DecisionParentActivity.Absorb, "only valid in a child-fault evaluation")]
    [InlineData(DecisionParentActivity.Intervene, "only valid in a child-fault evaluation")]
    [InlineData(DecisionParentActivity.CancelAndFault, "cannot also cancel child subtrees")]
    public async Task A_decision_evaluation_that_misuses_a_seam_faults_the_requester(string decision, string expectedMessage)
    {
        await using var fixture = await DecisionFixture.StartAsync(new DecisionDirective(Offer));
        var incidentId = Assert.Single(await fixture.ListIncidentsAsync()).IncidentId;

        await fixture.DecideAsync(incidentId, decision, $"decide-{decision}");

        var parent = (await fixture.ReadRunAsync()).State("node-parent");
        Assert.Equal(ActivityExecutionStatus.Faulted, parent.Status);
        Assert.Contains(expectedMessage, parent.Fault!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_repeated_delivery_is_acked_by_the_processed_marker()
    {
        var recorder = new EnqueuedWorkItemRecorder();
        await using var fixture = await DecisionFixture.StartAsync(new DecisionDirective(Offer) { KeepRunningOnComplete = true }, recorder: recorder);
        var incidentId = Assert.Single(await fixture.ListIncidentsAsync()).IncidentId;
        Assert.Equal(WorkflowAlterationJobStatus.Succeeded, (await fixture.DecideAsync(incidentId, Complete, "decide-repeat")).Status);
        var delivery = Assert.Single(recorder.Items, item => item.CommandKind == WorkflowExecutionCommandKind.DeliverInterventionDecision);
        Assert.Single(fixture.Log.Decisions);

        await fixture.DeliverAgainAsync(delivery);

        Assert.Single(fixture.Log.Decisions);
        Assert.Equal(delivery.WorkItemId, (await fixture.ReadRunAsync()).State("node-parent").Metadata[RuntimeMetadataKeys.InterventionDecisionProcessedWorkItemId]);
    }

    [Fact]
    public async Task A_requester_that_is_no_longer_running_acks_the_delivery_without_a_callback()
    {
        var recorder = new EnqueuedWorkItemRecorder();
        await using var fixture = await DecisionFixture.StartAsync(new DecisionDirective(Offer), recorder: recorder,
            configure: services => DecisionFixture.StopBeforeDelivery(services));
        var incidentId = Assert.Single(await fixture.ListIncidentsAsync()).IncidentId;

        // The alteration commits and the host stops before the decision reaches the requester.
        Assert.Equal(WorkflowAlterationJobStatus.Succeeded, (await fixture.DecideAsync(incidentId, Complete, "decide-cancelled", tolerateStop: true)).Status);
        Assert.Empty(fixture.Log.Decisions);

        // The requester ends before the delivery runs (here: its run is cancelled, which cancels it).
        await fixture.CancelRequesterAsync();
        await fixture.SweepAsync();

        Assert.Empty(fixture.Log.Decisions);
        var incident = Assert.Single(await fixture.ListIncidentsAsync());
        Assert.Equal(IncidentResolutionActionKinds.OperatorDecision, incident.ResolutionOutcome!.ActionKind);
    }

    [Fact]
    public async Task A_stop_between_the_alteration_checkpoint_and_the_delivery_applies_the_decision_once_after_restart()
    {
        await using var fixture = await DecisionFixture.StartAsync(new DecisionDirective(Offer), configure: services => DecisionFixture.StopBeforeDelivery(services));
        var incidentId = Assert.Single(await fixture.ListIncidentsAsync()).IncidentId;

        Assert.Equal(WorkflowAlterationJobStatus.Succeeded, (await fixture.DecideAsync(incidentId, Continue, "decide-stop", tolerateStop: true)).Status);
        Assert.Empty(fixture.Log.Decisions);
        Assert.Equal(IncidentStatus.Resolved, Assert.Single(await fixture.ListIncidentsAsync()).Status);

        await fixture.SweepAsync();

        Assert.Single(fixture.Log.Decisions);
        var run = await fixture.ReadRunAsync();
        run.AssertWorkflowCompleted();
        Assert.Single(run.States("node-next"));
    }

    [Fact]
    public async Task The_same_idempotency_key_twice_is_replayed_and_applies_once()
    {
        await using var fixture = await DecisionFixture.StartAsync(new DecisionDirective(Offer));
        var incidentId = Assert.Single(await fixture.ListIncidentsAsync()).IncidentId;

        var first = await fixture.SubmitAsync(incidentId, Continue, "decide-same");
        var replay = await fixture.SubmitAsync(incidentId, Continue, "decide-same");
        await fixture.OrchestrateAsync(first.Plan.PlanId);

        Assert.False(first.IsReplay);
        Assert.True(replay.IsReplay);
        Assert.Equal(first.Plan.PlanId, replay.Plan.PlanId);
        Assert.Single(fixture.Log.Decisions);
        Assert.Single((await fixture.ReadRunAsync()).States("node-next"));
    }

    [Fact]
    public async Task A_second_key_on_the_same_incident_is_refused_as_stale_and_a_retry_after_it_finds_no_blocking_incident()
    {
        await using var fixture = await DecisionFixture.StartAsync(new DecisionDirective(Offer) { KeepRunningOnComplete = true });
        var incidentId = Assert.Single(await fixture.ListIncidentsAsync()).IncidentId;
        var sourceId = fixture.Run.State("node-f").Execution.ActivityExecutionId;
        Assert.Equal(WorkflowAlterationJobStatus.Succeeded, (await fixture.DecideAsync(incidentId, Complete, "decide-first")).Status);

        var second = await fixture.DecideAsync(incidentId, Continue, "decide-second");
        var retry = await fixture.RetryAsync(sourceId, "retry-after-decision");

        Assert.Equal(WorkflowAlterationJobStatus.Failed, second.Status);
        Assert.Equal("InterventionNotPending", Assert.Single(second.Outcomes).Code);
        Assert.Equal(WorkflowAlterationJobStatus.Failed, retry.Status);
        Assert.Equal("NoBlockingIncident", Assert.Single(retry.Outcomes).Code);
        Assert.Single(fixture.Log.Decisions);
        Assert.Single((await fixture.ReadRunAsync()).States("node-f"));
    }

    [Fact]
    public async Task A_decision_that_was_not_offered_is_refused()
    {
        await using var fixture = await DecisionFixture.StartAsync(new DecisionDirective([Complete]));
        var incidentId = Assert.Single(await fixture.ListIncidentsAsync()).IncidentId;

        var job = await fixture.DecideAsync(incidentId, Continue, "decide-not-offered");

        Assert.Equal(WorkflowAlterationJobStatus.Failed, job.Status);
        Assert.Equal("DecisionNotOffered", Assert.Single(job.Outcomes).Code);
        Assert.Empty(fixture.Log.Decisions);
        Assert.Equal(IncidentStatus.Blocking, Assert.Single(await fixture.ListIncidentsAsync()).Status);
    }

    [Fact]
    public void The_payload_checks_its_arguments()
    {
        var identity = WorkflowExecutionHarness.Identity;
        Assert.Throws<ArgumentException>(() => NewPayload(identity, decision: "Not a code"));
        Assert.Throws<ArgumentException>(() => NewPayload(identity, incidentId: " "));
        Assert.Throws<ArgumentException>(() => NewPayload(identity, faulted: "requester"));
        Assert.Throws<ArgumentException>(() => NewPayload(identity, parent: " "));
        Assert.Throws<ArgumentNullException>(() => NewPayload(null!));
        Assert.Equal("test.ok", NewPayload(identity).Decision);
    }

    private static RuntimeInterventionDecisionCommandPayload NewPayload(
        WorkflowExecutableIdentity identity,
        string decision = "test.ok",
        string incidentId = "incident",
        string faulted = "faulted",
        string? parent = null) =>
        new(identity, "node", "requester", parent, null, incidentId, decision, faulted, "faulted-node", "reason");

    private sealed class DecisionFixture : IAsyncDisposable
    {
        public const string Operator = "operator-1";
        private static readonly WorkflowAlterationAuthorityScope Authority =
            new(WorkflowExecutionHarness.Partition.Value, "activity-behavioural-drive", "activity-behavioural-drive");

        private readonly WorkflowExecutionHarness _harness;

        private DecisionFixture(WorkflowExecutionHarness harness, DecisionLog log, WorkflowExecutionRun run)
        {
            _harness = harness;
            Log = log;
            Run = run;
        }

        public DecisionLog Log { get; }
        public WorkflowExecutionRun Run { get; }

        public static async Task<DecisionFixture> StartAsync(
            DecisionDirective directive,
            Type? parentType = null,
            EnqueuedWorkItemRecorder? recorder = null,
            Action<IServiceCollection>? configure = null)
        {
            var log = new DecisionLog();
            var harness = WorkflowExecutionHarness.Create()
                .ConfigureServices(services =>
                {
                    services.AddSingleton(directive);
                    services.AddSingleton(log);
                    services.AddSingleton(new ChildEvaluationLog());
                    services.AddSingleton(new OperationKeyLog());
                    services.AddSingleton(new FailureBudget(1));
                    services.TryAddScoped<IRuntimeResumptionService, RuntimeResumptionService>();
                    if (recorder is not null)
                        RecordingSchedulerWorkQueue.Register(services, recorder);
                    configure?.Invoke(services);
                })
                .Build("actexec-parent", "actexec-f", "actexec-next");
            var executable = StructuralExecutionTestSupport.NewExecutable(
                StructuralExecutionTestSupport.NewStructuralNode("node-parent", parentType ?? typeof(DecisionParentActivity),
                    InterventionTestSupport.NewLeafNode("node-f", typeof(OutcomeUnknownActivity)),
                    WorkflowExecutionHarness.NewProbeNode("node-next")));
            var reference = await harness.PublishAsync(executable, "decision-source");
            await harness.StartPublishedAsync(reference, harness.ExecutionId, tenantId: WorkflowExecutionHarness.Partition.Value);
            return new DecisionFixture(harness, log, await harness.ReadRunAsync(harness.ExecutionId));
        }

        // Stops the host once, before the first decision delivery is evaluated; the work item stays queued.
        public static void StopBeforeDelivery(IServiceCollection services)
        {
            var descriptor = services.Single(candidate =>
                candidate.ServiceType == typeof(IWorkflowSchedulerWorkHandler) &&
                candidate.ImplementationType == typeof(WorkflowInterventionDecisionSchedulerWorkHandler));
            services.Remove(descriptor);
            var stopped = 0;
            services.Add(ServiceDescriptor.Describe(
                typeof(IWorkflowSchedulerWorkHandler),
                serviceProvider => new StoppingHandler(
                    ActivatorUtilities.CreateInstance<WorkflowInterventionDecisionSchedulerWorkHandler>(serviceProvider),
                    () => Interlocked.Exchange(ref stopped, 1) == 0),
                descriptor.Lifetime));
        }

        public async Task<WorkflowAlterationJobState> DecideAsync(string incidentId, string decision, string idempotencyKey, string reason = "Operator decision", bool tolerateStop = false)
        {
            var admission = await SubmitAsync(incidentId, decision, idempotencyKey, reason);
            return await OrchestrateAsync(admission.Plan.PlanId, tolerateStop);
        }

        public Task<WorkflowAlterationPlanAdmissionResult> SubmitAsync(string incidentId, string decision, string idempotencyKey, string reason = "Operator decision") =>
            SubmitEnvelopeAsync(
                new WorkflowAlterationEnvelope(DecideInterventionAlterationHandler.Kind, DecideInterventionAlterationHandler.SchemaVersion,
                    JsonSerializer.SerializeToElement(new { incidentId, decision, reason })),
                idempotencyKey);

        public async Task<WorkflowAlterationJobState> RetryAsync(string sourceActivityExecutionId, string idempotencyKey)
        {
            var admission = await SubmitEnvelopeAsync(
                new WorkflowAlterationEnvelope(RetryFaultedActivityAlterationHandler.Kind, RetryFaultedActivityAlterationHandler.SchemaVersion,
                    JsonSerializer.SerializeToElement(new { sourceActivityExecutionId })),
                idempotencyKey);
            return await OrchestrateAsync(admission.Plan.PlanId);
        }

        private async Task<WorkflowAlterationPlanAdmissionResult> SubmitEnvelopeAsync(WorkflowAlterationEnvelope envelope, string idempotencyKey)
        {
            await using var scope = _harness.Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<WorkflowAlterationPlanService>().SubmitAsync(
                new WorkflowAlterationSubmission(Authority, WorkflowAlterationTargetSelector.ForExecutionIds([_harness.ExecutionId]), [envelope]),
                new WorkflowAlterationOperatorProvenance(Operator, correlationId: null),
                idempotencyKey);
        }

        public async Task<WorkflowAlterationJobState> OrchestrateAsync(string planId, bool tolerateStop = false)
        {
            var jobId = WorkflowAlterationIdentity.CreateJobId(planId, _harness.ExecutionId);
            var options = new WorkflowAlterationOrchestrationOptions { WorkerId = "test-worker" };
            WorkflowAlterationJobState? job = null;
            for (var sweep = 0; sweep < 8; sweep++)
            {
                await using (var scope = _harness.Services.CreateAsyncScope())
                {
                    await scope.ServiceProvider.GetRequiredService<WorkflowAlterationOrchestrationSweep>().ExecuteAsync(options, "test-worker");
                    job = await scope.ServiceProvider.GetRequiredService<IWorkflowAlterationStore>().FindJobAsync(jobId);
                }

                if (job?.Status is WorkflowAlterationJobStatus.Succeeded or WorkflowAlterationJobStatus.Failed or WorkflowAlterationJobStatus.Cancelled)
                    break;
            }

            Assert.NotNull(job);
            if (tolerateStop)
            {
                try
                {
                    await _harness.SweepUntilQuietAsync();
                }
                catch (OperationCanceledException)
                {
                    // The host stopped before the delivery, as a process shutdown stops it.
                }
            }
            else
            {
                await _harness.SweepUntilQuietAsync();
            }

            return job!;
        }

        public Task SweepAsync() => _harness.SweepUntilQuietAsync();

        // Cancels the requester the way a cancelled scope ends: its state is committed as Cancelled.
        public async Task CancelRequesterAsync()
        {
            var store = _harness.Services.GetRequiredService<IActivityExecutionStateStore>();
            var requester = (await store.FindAsync(_harness.ExecutionId, "actexec-parent"))!;
            await store.SaveAsync(requester with { Status = ActivityExecutionStatus.Cancelled, CompletedAt = WorkflowExecutionHarness.Timestamp });
        }

        public async Task DeliverAgainAsync(RuntimeSchedulerWorkItem delivery)
        {
            var handler = ActivatorUtilities.CreateInstance<WorkflowInterventionDecisionSchedulerWorkHandler>(_harness.Services);
            await handler.HandleAsync(delivery);
        }

        public Task<WorkflowExecutionRun> ReadRunAsync() => _harness.ReadRunAsync(_harness.ExecutionId);

        public async Task<IReadOnlyCollection<IncidentState>> ListIncidentsAsync() =>
            await _harness.Services.GetRequiredService<IIncidentStateStore>().ListAsync(_harness.ExecutionId);

        public ValueTask DisposeAsync() => _harness.DisposeAsync();

        private sealed class StoppingHandler(IWorkflowSchedulerWorkHandler inner, Func<bool> stopNow) : IWorkflowSchedulerWorkHandler
        {
            public string Name => inner.Name;

            public bool CanHandle(RuntimeSchedulerWorkItem workItem) => inner.CanHandle(workItem);

            public ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default) =>
                stopNow()
                    ? throw new OperationCanceledException("The host stopped during the pass.")
                    : inner.HandleAsync(workItem, cancellationToken);
        }
    }
}

/// <summary>The offer the <see cref="DecisionParentActivity"/> makes when its child faults.</summary>
public sealed record DecisionDirective(IReadOnlyCollection<string>? Offer)
{
    /// <summary>When set, the complete decision defers instead of completing, so the requester stays running.</summary>
    public bool KeepRunningOnComplete { get; init; }

    /// <summary>When set, the request metadata also carries the runtime's reserved offer keys, as a forged offer.</summary>
    public bool ForgesOfferMetadata { get; init; }
}

/// <summary>Records every decision the <see cref="DecisionParentActivity"/> received, in order.</summary>
public sealed class DecisionLog
{
    private readonly List<ActivityInterventionDecidedContext> _decisions = [];
    public IReadOnlyList<ActivityInterventionDecidedContext> Decisions => _decisions;
    public void Add(ActivityInterventionDecidedContext decision) => _decisions.Add(decision);
}

/// <summary>
/// Schedules its first slot child. On that child's fault it requests intervention with the directive's offer; on a
/// decision it continues with its second child, completes, faults or misuses a seam, as the decision code says.
/// </summary>
public sealed class DecisionParentActivity(DecisionDirective directive, DecisionLog log) : StructuralActivity,
    IRuntimeStructuralActivity,
    IRuntimeActivityChildCompletionHandler,
    IRuntimeActivityChildFaultHandler,
    IRuntimeActivityInterventionDecisionHandler
{
    public const string Continue = "test.continue";
    public const string Complete = "test.complete";
    public const string Fault = "test.fault";
    public const string Absorb = "test.misuse.absorb";
    public const string Intervene = "test.misuse.intervene";
    public const string CancelAndFault = "test.misuse.cancel-and-fault";
    public const string DecidedFaultCode = "test.decided-fault";

    public ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext context)
    {
        var first = Assert.Single(context.ExecutableNode.ChildSlots).Activities.ElementAt(0);
        context.ScheduleChildActivity(first.ExecutableNodeId, context.ActivityExecutionState.InvocationId);
        return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
    }

    public ValueTask<RuntimeStructuralContinuation> OnChildFaultedAsync(ActivityChildFaultedContext context)
    {
        var runtimeContext = (IRuntimeActivityExecutionContext)context.ParentContext;
        var metadata = new Dictionary<string, string> { ["test.scope"] = "scope-1" };
        if (directive.ForgesOfferMetadata)
        {
            metadata[RuntimeMetadataKeys.InterventionDecisions] = JsonSerializer.Serialize(new[] { "test.forged" });
            metadata[RuntimeMetadataKeys.InterventionRequestedBy] = "actexec-forged";
        }

        if (directive.Offer is null)
            runtimeContext.RequestChildFaultIntervention(context.IncidentId!, "test-intervene", metadata);
        else
            runtimeContext.RequestChildFaultIntervention(context.IncidentId!, "test-intervene", metadata, directive.Offer);
        return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
    }

    public ValueTask<RuntimeStructuralContinuation> OnChildCompletedAsync(ActivityChildCompletedContext context) =>
        ValueTask.FromResult(RuntimeStructuralContinuation.Complete());

    public ValueTask<RuntimeStructuralContinuation> OnInterventionDecidedAsync(IRuntimeActivityExecutionContext context, ActivityInterventionDecidedContext decision)
    {
        log.Add(decision);
        switch (decision.Decision)
        {
            case Continue:
                var next = Assert.Single(context.ExecutableNode.ChildSlots).Activities.ElementAt(1);
                context.ScheduleChildActivity(next.ExecutableNodeId, decision.ViaChildActivityExecutionId);
                return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
            case Complete:
                return ValueTask.FromResult(directive.KeepRunningOnComplete ? RuntimeStructuralContinuation.Defer : RuntimeStructuralContinuation.Complete());
            case Fault:
                return ValueTask.FromResult(RuntimeStructuralContinuation.Faulted(new ActivityFault(DecidedFaultCode, "The operator decided to fault.")));
            case Absorb:
                context.RequestChildFaultAbsorption(decision.IncidentId, "misuse");
                return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
            case Intervene:
                context.RequestChildFaultIntervention(decision.IncidentId, "misuse");
                return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
            default:
                context.RequestChildSubtreeCancellation(decision.ViaChildActivityExecutionId, "misuse");
                return ValueTask.FromResult(RuntimeStructuralContinuation.Faulted(new ActivityFault(DecidedFaultCode, "Fault with a cancellation.")));
        }
    }
}

/// <summary>Offers decisions on a child fault without implementing the decision callback (seam misuse).</summary>
public sealed class OfferWithoutCallbackParentActivity(DecisionDirective directive) : StructuralActivity,
    IRuntimeStructuralActivity,
    IRuntimeActivityChildCompletionHandler,
    IRuntimeActivityChildFaultHandler
{
    public ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext context)
    {
        var first = Assert.Single(context.ExecutableNode.ChildSlots).Activities.ElementAt(0);
        context.ScheduleChildActivity(first.ExecutableNodeId, context.ActivityExecutionState.InvocationId);
        return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
    }

    public ValueTask<RuntimeStructuralContinuation> OnChildFaultedAsync(ActivityChildFaultedContext context)
    {
        ((IRuntimeActivityExecutionContext)context.ParentContext).RequestChildFaultIntervention(context.IncidentId!, "test-intervene", null, directive.Offer!);
        return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
    }

    public ValueTask<RuntimeStructuralContinuation> OnChildCompletedAsync(ActivityChildCompletedContext context) =>
        ValueTask.FromResult(RuntimeStructuralContinuation.Complete());
}
