using System.Text.Json;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
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

namespace Elsa.Activities.Runtime.Tests.VariableLifecycle;

/// <summary>
/// US2 (spec 1221 T017–T019, FR-014/FR-015, FR-023; ADR 0092 decision 8 as amended 2026-11-16). A faulted step reads
/// <c>count = 5</c>; the operator corrects <c>count</c> to 10 with <c>ModifyVariable</c> and then:
/// <list type="bullet">
/// <item>re-executes it: a new logical invocation on fresh inputs, so it sees 10 and writes 11;</item>
/// <item>or retries it: the same logical invocation on its committed inputs, so it sees 5 and writes 6 on a stale
/// base, which applies with a lost-update warning that names the operator's change and keeps 10 for restoring.</item>
/// </list>
/// </summary>
public sealed class OperatorActionsTests
{
    private const string Operator = "operator-1";
    private static readonly WorkflowAlterationAuthorityScope Authority =
        new(WorkflowExecutionHarness.Partition.Value, "activity-behavioural-drive", "activity-behavioural-drive");

    [Fact]
    public async Task ModifyVariable_then_re_execute_runs_a_new_invocation_on_the_corrected_value()
    {
        await using var fixture = await Fixture.StartAsync();
        var source = fixture.Run.State("node-f");
        Assert.Equal(5, Assert.Single(fixture.Reads.Observed).Count);

        Assert.Equal(WorkflowAlterationJobStatus.Succeeded, (await fixture.ModifyCountAsync(10)).Status);
        var job = await fixture.ApplyAsync(ReExecuteFaultedActivityAlterationHandler.Kind, source.Execution.ActivityExecutionId);

        Assert.Equal(WorkflowAlterationJobStatus.Succeeded, job.Status);
        var run = await fixture.ReadRunAsync();
        run.AssertWorkflowCompleted();
        var successor = Assert.Single(run.States("node-f"), state => state.Status == ActivityExecutionStatus.Completed);
        // Fresh inputs: the successor read the corrected value, under a new logical identity and operation key.
        Assert.Equal(10, fixture.Reads.Observed[1].Count);
        Assert.Equal(10, successor.InputSnapshot!.Values["Count"].InlineValue!.Value.GetInt32());
        Assert.Equal(successor.InvocationId, successor.LogicalInvocationId);
        Assert.NotEqual(source.LogicalInvocationId, successor.LogicalInvocationId);
        // The activity sees its logical identity on the context: the source's for the first run, the new row's now.
        Assert.Equal(source.InvocationId, fixture.Reads.Observed[0].LogicalInvocationId);
        Assert.Equal(successor.InvocationId, fixture.Reads.Observed[1].LogicalInvocationId);
        Assert.NotEqual(fixture.Reads.Observed[0].OperationKey, fixture.Reads.Observed[1].OperationKey);
        Assert.Equal(1, Assert.Single(successor.InputSnapshot.InputReadSet!.Entries).Version);
        Assert.Equal(11, VariableLifecycleTestGraph.ReadCount(run));
        Assert.Equal(VariableCommitOutcome.Current, Assert.Single(successor.VariableWrites!).Outcome);
        var incident = Assert.Single(await fixture.ListIncidentsAsync());
        Assert.Equal("ReExecute", incident.ResolutionOutcome!.Metadata["runtime.alteration.operatorAction"]);
    }

    [Fact]
    public async Task Retry_keeps_the_logical_invocation_and_its_inputs_and_warns_over_the_operators_value()
    {
        await using var fixture = await Fixture.StartAsync();
        var source = fixture.Run.State("node-f");

        Assert.Equal(WorkflowAlterationJobStatus.Succeeded, (await fixture.ModifyCountAsync(10)).Status);
        var job = await fixture.ApplyAsync(RetryFaultedActivityAlterationHandler.Kind, source.Execution.ActivityExecutionId);

        Assert.Equal(WorkflowAlterationJobStatus.Succeeded, job.Status);
        var run = await fixture.ReadRunAsync();
        run.AssertWorkflowCompleted();
        var successor = Assert.Single(run.States("node-f"), state => state.Status == ActivityExecutionStatus.Completed);
        // Same logical invocation, same operation key and committed inputs; a new row and attempt.
        Assert.NotEqual(source.InvocationId, successor.InvocationId);
        Assert.Equal(source.LogicalInvocationId, successor.LogicalInvocationId);
        // ...and the activity sees the same logical identity on both runs, though its row id changed.
        Assert.Equal(source.InvocationId, fixture.Reads.Observed[0].LogicalInvocationId);
        Assert.Equal(source.InvocationId, fixture.Reads.Observed[1].LogicalInvocationId);
        Assert.Equal(2, successor.Attempt!.AttemptNumber);
        Assert.Equal(fixture.Reads.Observed[0].OperationKey, fixture.Reads.Observed[1].OperationKey);
        Assert.Equal(5, fixture.Reads.Observed[1].Count);
        // The retried write is built on the stale base 0 while the operator moved count to version 1: it applies (last
        // write wins) with a warning that names the operator's change and keeps its value for restoring.
        Assert.Equal(6, VariableLifecycleTestGraph.ReadCount(run));
        var record = Assert.Single(successor.VariableWrites!);
        Assert.Equal(VariableCommitOutcome.LostUpdateWarning, record.Outcome);
        Assert.Equal(VariableWriteSource.Alteration, record.OverwrittenWriter!.Source);
        Assert.Equal(10, record.OverwrittenValue!.InlineValue!.Value.GetInt32());
    }

    // T020 / FR-016: a re-executed loop body owns exactly one iteration frame, activated once by its own Start for the
    // same iteration, and reads the loop item from it again. The source's activated frame is not carried over and not
    // activated a second time.
    [Fact]
    public async Task A_re_executed_loop_body_activates_its_iteration_frame_once_and_reads_the_item_again()
    {
        await using var fixture = await Fixture.StartAsync(LoopExecutable(), "actexec-foreach", "actexec-body");
        var source = fixture.Run.State(LoopBodyNodeId);
        Assert.Equal(ActivityExecutionStatus.Suspended, source.Status);
        var sourceFrame = source.IterationVariableFrame!;

        var job = await fixture.ApplyAsync("RescheduleActivity", source.Execution.ActivityExecutionId);

        Assert.Equal(WorkflowAlterationJobStatus.Succeeded, job.Status);
        var run = await fixture.ReadRunAsync();
        var successor = Assert.Single(run.States(LoopBodyNodeId), state => state.Status == ActivityExecutionStatus.Suspended);
        Assert.NotEqual(source.InvocationId, successor.InvocationId);
        // One iteration frame, activated by the successor's own Start for the same iteration and item.
        var frame = successor.IterationVariableFrame!;
        Assert.Null(successor.IterationFrameRequest);
        Assert.NotEqual(sourceFrame.FrameId, frame.FrameId);
        Assert.Equal(sourceFrame.ScopeId, frame.ScopeId);
        Assert.StartsWith($"{successor.InvocationId}:iteration:", frame.ActivationId, StringComparison.Ordinal);
        Assert.Equal(source.IterationId, successor.IterationId);
        Assert.Equal("first", frame.Values[ForEachCurrentItem].InlineValue!.Value.GetString());
        Assert.Equal(["first", "first"], fixture.Reads.Items);
        // The source's frame is untouched: it was not activated again or moved to the successor.
        var superseded = Assert.Single(run.States(LoopBodyNodeId), state => state.Status == ActivityExecutionStatus.Superseded);
        Assert.Equal(sourceFrame, superseded.IterationVariableFrame);
    }

    // T020 / FR-016, one iteration recovered: a ForEach over three items whose body faults on the second. Under an
    // ancestor that waits for intervention, the operator recovers iteration 2 only: iteration 1 is not run again, the
    // recovered body reads its own item again, and iteration 3, which never ran, runs right after it.
    [Theory]
    [InlineData(ReExecuteFaultedActivityAlterationHandler.Kind)]
    [InlineData(RetryFaultedActivityAlterationHandler.Kind)]
    public async Task Recovering_a_faulted_iteration_runs_only_that_iteration_again_and_then_the_ones_that_never_ran(string kind)
    {
        await using var fixture = await Fixture.StartAsync(ThreeItemLoopExecutable(underInterventionReceiver: true),
            "actexec-receiver", "actexec-foreach", "actexec-body-1", "actexec-body-2", "actexec-body-3");
        var run = fixture.Run;
        Assert.Equal(WorkflowExecutionStatus.Running, run.WorkflowState!.Status);
        Assert.Equal(["first", "second"], fixture.Reads.Items);
        var faulted = Assert.Single(run.States(LoopBodyNodeId), state => state.Status == ActivityExecutionStatus.Faulted);
        var firstIteration = Assert.Single(run.States(LoopBodyNodeId), state => state.Status == ActivityExecutionStatus.Completed);
        var incident = Assert.Single(await fixture.ListIncidentsAsync());
        Assert.Equal(IncidentResolutionActionKinds.WaitForIntervention, incident.ResolutionOutcome?.ActionKind);

        var job = await fixture.ApplyAsync(kind, faulted.Execution.ActivityExecutionId);

        Assert.Equal(WorkflowAlterationJobStatus.Succeeded, job.Status);
        run = await fixture.ReadRunAsync();
        run.AssertWorkflowCompleted();
        // Iteration 1 ran once; iteration 2 ran twice, the second time on its own item; iteration 3 ran once, after it.
        Assert.Equal(["first", "second", "second", "third"], fixture.Reads.Items);
        var rows = run.States(LoopBodyNodeId);
        Assert.Equal(4, rows.Count);
        var firstAfter = Assert.Single(rows, state => state.Execution.ActivityExecutionId == firstIteration.Execution.ActivityExecutionId);
        Assert.Equal(ActivityExecutionStatus.Completed, firstAfter.Status);
        Assert.Equal(firstIteration.InvocationId, firstAfter.InvocationId);
        var superseded = Assert.Single(rows, state => state.Status == ActivityExecutionStatus.Superseded);
        Assert.Equal(faulted.Execution.ActivityExecutionId, superseded.Execution.ActivityExecutionId);
        var recovered = Assert.Single(rows, state => state.IterationId == faulted.IterationId && state.Status == ActivityExecutionStatus.Completed);
        Assert.Equal("second", recovered.IterationVariableFrame!.Values[ForEachCurrentItem].InlineValue!.Value.GetString());
        Assert.Equal(kind == RetryFaultedActivityAlterationHandler.Kind, recovered.LogicalInvocationId == faulted.LogicalInvocationId);
        var third = Assert.Single(rows, state => state.Status == ActivityExecutionStatus.Completed &&
            state.IterationId != firstIteration.IterationId && state.IterationId != faulted.IterationId);
        Assert.Equal("third", third.IterationVariableFrame!.Values[ForEachCurrentItem].InlineValue!.Value.GetString());
    }

    // The same loop under the workflow's Fault strategy and no intervening ancestor: the fault ends the run, iteration 3
    // never runs, and neither recovery action can bring a terminal run back.
    [Theory]
    [InlineData(ReExecuteFaultedActivityAlterationHandler.Kind)]
    [InlineData(RetryFaultedActivityAlterationHandler.Kind)]
    public async Task Under_the_fault_strategy_a_faulted_iteration_ends_the_run_and_cannot_be_recovered(string kind)
    {
        await using var fixture = await Fixture.StartAsync(ThreeItemLoopExecutable(underInterventionReceiver: false),
            "actexec-foreach", "actexec-body-1", "actexec-body-2", "actexec-body-3");
        Assert.Equal(WorkflowExecutionStatus.Faulted, fixture.Run.WorkflowState!.Status);
        Assert.Equal(["first", "second"], fixture.Reads.Items);
        var faulted = Assert.Single(fixture.Run.States(LoopBodyNodeId), state => state.Status == ActivityExecutionStatus.Faulted);

        var job = await fixture.ApplyAsync(kind, faulted.Execution.ActivityExecutionId);

        Assert.Equal(WorkflowAlterationJobStatus.Failed, job.Status);
        Assert.Equal("WorkflowTerminal", Assert.Single(job.Outcomes).Code);
        Assert.Equal(["first", "second"], fixture.Reads.Items);
        Assert.Equal(2, (await fixture.ReadRunAsync()).States(LoopBodyNodeId).Count);
    }

    private const string LoopBodyNodeId = "node-body";
    private const string ForEachCurrentItem = Elsa.Activities.ForEach.Activities.ForEach.CurrentItemVariableName;
    private static readonly string LoopResumeTargetId = $"resume:{LoopBodyNodeId}:{LoopItemWaitingActivity.ResumeTargetKey}";

    private static WorkflowExecutable LoopExecutable() =>
        LoopExecutable(typeof(LoopItemWaitingActivity), ["first", "second"], underInterventionReceiver: false);

    private static WorkflowExecutable ThreeItemLoopExecutable(bool underInterventionReceiver) =>
        LoopExecutable(typeof(LoopItemFlakyActivity), ["first", "second", "third"], underInterventionReceiver);

    private static WorkflowExecutable LoopExecutable(Type bodyType, string[] items, bool underInterventionReceiver)
    {
        var itemType = new Elsa.Primitives.Models.ValueTypeDescriptor("String");
        var listType = new Elsa.Primitives.Models.ValueTypeDescriptor("String", Elsa.Primitives.Models.CollectionKind.List);
        var body = new ExecutableNode(
            executableNodeId: LoopBodyNodeId,
            authoredActivityId: $"authored-{LoopBodyNodeId}",
            activityType: bodyType.FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: "test",
            descriptorPayload: JsonSerializer.SerializeToElement(new { }),
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                [nameof(LoopItemWaitingActivity.Item)] = new(
                    nameof(LoopItemWaitingActivity.Item),
                    itemType,
                    ValueProtectionPolicy.InstanceInline,
                    RuntimeInputBindingSource.VariableRead,
                    variable: new RuntimeVariableReference(ForEachCurrentItem, "node-foreach"))
            },
            metadata: new Dictionary<string, string>());
        var forEach = new ExecutableNode(
            executableNodeId: "node-foreach",
            authoredActivityId: "authored-node-foreach",
            activityType: typeof(Elsa.Activities.ForEach.Activities.ForEach).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: "test",
            descriptorPayload: JsonSerializer.SerializeToElement(new { }),
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                [nameof(Elsa.Activities.ForEach.Activities.ForEach.Collection)] = new(
                    nameof(Elsa.Activities.ForEach.Activities.ForEach.Collection),
                    listType,
                    ValueProtectionPolicy.InstanceInline,
                    RuntimeInputBindingSource.Literal,
                    literal: ValueEnvelope.Inline(listType, JsonSerializer.SerializeToElement(items), ValueProtectionPolicy.InstanceInline))
            },
            metadata: new Dictionary<string, string>(),
            childSlots: [new ExecutableChildSlot(Elsa.Activities.ForEach.Activities.ForEach.BodySlotName, [body])],
            structure: new ExecutableActivityStructure(
                Elsa.Activities.ForEach.Activities.ForEach.StructureKind,
                Elsa.Activities.ForEach.Activities.ForEach.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new { body = LoopBodyNodeId })));
        return new WorkflowExecutable(
            identity: WorkflowExecutionHarness.Identity,
            rootActivity: underInterventionReceiver
                ? StructuralExecutionTestSupport.NewStructuralNode("node-receiver", typeof(DescendantFaultExecutionTests.DescendantFaultReceiverActivity), forEach)
                : forEach,
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(StringComparer.Ordinal)
            {
                [LoopResumeTargetId] = new(LoopResumeTargetId, LoopBodyNodeId, "ResumeAsync", new Dictionary<string, string>(),
                    LocalResumeTargetId: LoopItemWaitingActivity.ResumeTargetKey)
            },
            createdAt: WorkflowExecutionHarness.Timestamp,
            compatibilityMetadata: new Dictionary<string, string>(),
            inputContract: null,
            dependencies: null,
            runtimeRequirements: null,
            storageDriverRequirements: null,
            incidentStrategy: IncidentStrategyBuiltIns.FaultReference);
    }

    /// <summary>A structural parent that waits for intervention over a leaf that reads <c>count</c> and faults once.</summary>
    private sealed class Fixture : IAsyncDisposable
    {
        private readonly WorkflowExecutionHarness _harness;

        private Fixture(WorkflowExecutionHarness harness, CountReadLog reads, WorkflowExecutionRun run)
        {
            _harness = harness;
            Reads = reads;
            Run = run;
        }

        public CountReadLog Reads { get; }
        public WorkflowExecutionRun Run { get; }

        public static async Task<Fixture> StartAsync()
        {
            var fixture = await StartAsync(
                WorkflowExecutionHarness.NewExecutable(
                    StructuralExecutionTestSupport.NewStructuralNode("node-parent", typeof(InterventionParentActivity),
                        VariableLifecycleTestGraph.Increment("node-f", typeof(FlakyIncrementActivity))),
                    VariableLifecycleTestGraph.CountDeclaration(initial: 5)),
                "actexec-parent", "actexec-f");
            Assert.Equal(ActivityExecutionStatus.Faulted, fixture.Run.State("node-f").Status);
            return fixture;
        }

        public static async Task<Fixture> StartAsync(WorkflowExecutable executable, params string[] activityExecutionIds)
        {
            var reads = new CountReadLog();
            var harness = WorkflowExecutionHarness.Create()
                .WithFeature(services => new Elsa.Activities.ControlFlow.ActivitiesControlFlowFeature().ConfigureServices(services))
                .ConfigureServices(services =>
                {
                    // The parent assigns no operation keys, so each invocation derives its own (a parent-assigned key stays the parent's).
                    services.AddSingleton(new InterventionDirective { AssignKeys = false });
                    services.AddSingleton(new ChildEvaluationLog());
                    services.AddSingleton(new OperationKeyLog());
                    services.AddSingleton(new FailureBudget(1));
                    services.AddSingleton(reads);
                    services.AddSingleton(new DescendantFaultExecutionTests.DescendantFaultLog());
                    services.AddSingleton(new DescendantFaultExecutionTests.ReceiverDirective(Intervene: true));
                    services.TryAddScoped<IRuntimeResumptionService, RuntimeResumptionService>();
                })
                .Build(activityExecutionIds);
            var reference = await harness.PublishAsync(executable, "operator-actions");
            await harness.StartPublishedAsync(reference, harness.ExecutionId, tenantId: WorkflowExecutionHarness.Partition.Value);
            return new Fixture(harness, reads, await harness.ReadRunAsync(harness.ExecutionId));
        }

        public Task<WorkflowAlterationJobState> ModifyCountAsync(int value) =>
            SubmitAndOrchestrateAsync("ModifyVariable", JsonSerializer.SerializeToElement(new { variableKey = VariableLifecycleTestGraph.CountKey, value }), $"modify-{value}");

        public Task<WorkflowAlterationJobState> ApplyAsync(string kind, string sourceActivityExecutionId) =>
            SubmitAndOrchestrateAsync(kind, JsonSerializer.SerializeToElement(new { sourceActivityExecutionId }), $"{kind}-{sourceActivityExecutionId}");

        private async Task<WorkflowAlterationJobState> SubmitAndOrchestrateAsync(string kind, JsonElement payload, string idempotencyKey)
        {
            WorkflowAlterationPlanAdmissionResult admission;
            await using (var scope = _harness.Services.CreateAsyncScope())
            {
                admission = await scope.ServiceProvider.GetRequiredService<WorkflowAlterationPlanService>().SubmitAsync(
                    new WorkflowAlterationSubmission(
                        Authority,
                        WorkflowAlterationTargetSelector.ForExecutionIds([_harness.ExecutionId]),
                        [new WorkflowAlterationEnvelope(kind, 1, payload)]),
                    new WorkflowAlterationOperatorProvenance(Operator, correlationId: null),
                    idempotencyKey);
            }

            var jobId = WorkflowAlterationIdentity.CreateJobId(admission.Plan.PlanId, _harness.ExecutionId);
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
            await _harness.SweepUntilQuietAsync();
            return job!;
        }

        public Task<WorkflowExecutionRun> ReadRunAsync() => _harness.ReadRunAsync(_harness.ExecutionId);

        public async Task<IReadOnlyCollection<IncidentState>> ListIncidentsAsync() =>
            await _harness.Services.GetRequiredService<IIncidentStateStore>().ListAsync(_harness.ExecutionId);

        public ValueTask DisposeAsync() => _harness.DisposeAsync();
    }
}

/// <summary>What each run of the test leaves saw.</summary>
public sealed class CountReadLog
{
    private readonly List<(int Count, string OperationKey, string LogicalInvocationId)> _observed = [];
    private readonly List<string> _items = [];
    public IReadOnlyList<(int Count, string OperationKey, string LogicalInvocationId)> Observed { get { lock (_observed) return _observed.ToArray(); } }
    public IReadOnlyList<string> Items { get { lock (_items) return _items.ToArray(); } }
    internal void Add(int count, string operationKey, string logicalInvocationId) { lock (_observed) _observed.Add((count, operationKey, logicalInvocationId)); }
    internal void AddItem(string item) { lock (_items) _items.Add(item); }
}

/// <summary>A loop body that reads the loop item, records it and waits.</summary>
public sealed class LoopItemWaitingActivity(CountReadLog reads) : StatefulActivity<ActivityUnit, WaitState, WaitTrigger>
{
    public const string ResumeTargetKey = "wait-loop-item";

    [ActivityInput]
    public string Item { get; set; } = string.Empty;

    protected override ValueTask<ActivityTransition<ActivityUnit, WaitState>> ExecuteAsync(ActivityExecutionContext context)
    {
        reads.AddItem(Item);
        return ValueTask.FromResult(Suspend(
            new WaitState("waiting"),
            [new ActivityTriggerRegistration<WaitTrigger>(ResumeTargetKey, "TestWaitLoopItem", "wait:loop-item")]));
    }

    protected override ValueTask<ActivityTransition<ActivityUnit, WaitState>> ResumeAsync(ActivityResumeContext<WaitState, WaitTrigger> context) =>
        ValueTask.FromResult(Complete(ActivityUnit.Value, ActivityOutcomes.Done));
}

/// <summary>A loop body that reads the loop item, records it, and faults on <c>"second"</c> while its budget lasts.</summary>
public sealed class LoopItemFlakyActivity(FailureBudget budget, CountReadLog reads) : Activity
{
    [ActivityInput]
    public string Item { get; set; } = string.Empty;

    protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context)
    {
        reads.AddItem(Item);
        return ValueTask.FromResult(Item == "second" && budget.TakeFailure()
            ? ActivityTransition.Fault<ActivityUnit>(new ActivityFault(OutcomeUnknownActivity.Code, "The response was lost.", category: OutcomeUnknownActivity.Category))
            : ActivityTransition.Complete(ActivityUnit.Value));
    }
}

/// <summary>An External leaf that reads <c>Count</c>, faults while its budget lasts, and otherwise returns <c>Count + 1</c>.</summary>
public sealed class FlakyIncrementActivity(FailureBudget budget, CountReadLog reads) : Activity<Incremented>
{
    [ActivityInput]
    public int Count { get; set; }

    protected override ValueTask<ActivityTransition<Incremented>> ExecuteAsync(ActivityExecutionContext context)
    {
        reads.Add(Count, context.OperationKey, context.LogicalInvocationId);
        return ValueTask.FromResult(budget.TakeFailure()
            ? ActivityTransition.Fault<Incremented>(new ActivityFault(OutcomeUnknownActivity.Code, "The response was lost.", category: OutcomeUnknownActivity.Category))
            : ActivityTransition.Complete(new Incremented(Count + 1), ActivityOutcomes.Done));
    }
}
