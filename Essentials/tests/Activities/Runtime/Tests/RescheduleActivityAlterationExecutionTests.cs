using System.Text.Json;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Elsa.Workflows.Runtime.Services.Alterations;
using Elsa.Workflows.Runtime.Services.Recovery;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using ParallelActivity = Elsa.Activities.Parallel.Activities.Parallel;
using SequenceActivity = Elsa.Activities.Sequence.Activities.Sequence;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// <c>RescheduleActivity/1</c> end to end. A rescheduled suspended child's successor reaches a parent that routes on
/// its children's command metadata as an ordinary, routable child completion. A faulted child whose fault its parent
/// already evaluated can be rescheduled only under a running parent that accepts a rescheduled faulted child, such as
/// a Sequence, which then continues; any other parent refuses it (#3135).
/// </summary>
public sealed class RescheduleActivityAlterationExecutionTests
{
    private static readonly WorkflowAlterationAuthorityScope Authority =
        new(WorkflowExecutionHarness.Partition.Value, "activity-behavioural-drive", "activity-behavioural-drive");

    [Fact]
    public async Task RescheduledSuspendedLeaf_Completes_AndTheParentRoutesItsCompletion()
    {
        await using var fixture = await RescheduleFixture.StartAsync(typeof(SuspendOnceActivity));
        var source = fixture.Run.State("node-f");
        Assert.Equal(ActivityExecutionStatus.Suspended, source.Status);

        var job = await fixture.RescheduleAsync(source.Execution.ActivityExecutionId, "reschedule-suspended");

        Assert.Equal(WorkflowAlterationJobStatus.Succeeded, job.Status);
        var run = await fixture.ReadRunAsync();
        var successor = Assert.Single(run.States("node-f"), state => state.Status == ActivityExecutionStatus.Completed);
        Assert.Equal(source.Execution.ActivityExecutionId, successor.Metadata[RuntimeMetadataKeys.SupersedesActivityExecutionId]);
        var completion = Assert.Single(fixture.Log.Completions);
        Assert.Equal(successor.Execution.ActivityExecutionId, completion.ChildActivityExecutionId);
        // The parent routes the successor's completion with the scheduling metadata it gave the source.
        Assert.Equal("marker-node-f", successor.Provenance.Metadata[InterventionParentActivity.MarkerKey]);
        Assert.Equal("marker-node-f", completion.Marker);
        run.AssertCompleted("node-parent");
        run.AssertWorkflowCompleted();
    }

    [Fact]
    public async Task FaultedLeafWhoseFaultTheParentEvaluated_UnderAParentThatDoesNotAcceptIt_IsRefused()
    {
        // The parent deferred on the fault without handling it, and ContinueWithIncidents keeps the run running.
        await using var fixture = await RescheduleFixture.StartAsync(typeof(OutcomeUnknownActivity));
        var source = fixture.Run.State("node-f");
        Assert.Equal(ActivityExecutionStatus.Faulted, source.Status);
        Assert.True(source.Metadata.ContainsKey(RuntimeMetadataKeys.ParentCompletionProcessedWorkItemId));
        Assert.Equal(IncidentStatus.Open, Assert.Single(await fixture.ListIncidentsAsync()).Status);

        var job = await fixture.RescheduleAsync(source.Execution.ActivityExecutionId, "reschedule-faulted");

        Assert.Equal(WorkflowAlterationJobStatus.Failed, job.Status);
        Assert.Equal("ParentDoesNotAcceptRescheduledChild", Assert.Single(job.Outcomes).Code);
        var run = await fixture.ReadRunAsync();
        Assert.Equal(ActivityExecutionStatus.Faulted, Assert.Single(run.States("node-f")).Status);
        Assert.Equal(ActivityExecutionStatus.Running, run.State("node-parent").Status);
        Assert.Empty(fixture.Log.Completions);
    }

    [Fact]
    public async Task FaultedLeafUnderASequence_IsRescheduled_AndTheSequenceContinues()
    {
        // The node flag below stands in for what the compiler derives from this.
        Assert.True(typeof(IRuntimeAcceptsRescheduledFaultedChild).IsAssignableFrom(typeof(SequenceActivity)));
        // The sequence handles no child faults, so ContinueWithIncidents leaves it waiting on the faulted step.
        await using var fixture = await RescheduleFixture.StartAsync(
            NewSequenceNode(InterventionTestSupport.NewLeafNode("node-f", typeof(OutcomeUnknownActivity)), WorkflowExecutionHarness.NewProbeNode("node-b")),
            "actexec-sequence", "actexec-f", "actexec-b");
        var source = fixture.Run.State("node-f");
        Assert.Equal(ActivityExecutionStatus.Faulted, source.Status);
        Assert.True(source.Metadata.ContainsKey(RuntimeMetadataKeys.ParentCompletionProcessedWorkItemId));
        Assert.Equal(ActivityExecutionStatus.Running, fixture.Run.State("node-sequence").Status);
        Assert.Empty(fixture.Run.States("node-b"));

        var job = await fixture.RescheduleAsync(source.Execution.ActivityExecutionId, "reschedule-sequence-step");

        Assert.Equal(WorkflowAlterationJobStatus.Succeeded, job.Status);
        var run = await fixture.ReadRunAsync();
        var successor = Assert.Single(run.States("node-f"), state => state.Status == ActivityExecutionStatus.Completed);
        // The sequence processed the successor's own completion, not the source's fault evaluation again.
        Assert.NotEqual(
            source.Metadata[RuntimeMetadataKeys.ParentCompletionProcessedWorkItemId],
            successor.Metadata[RuntimeMetadataKeys.ParentCompletionProcessedWorkItemId]);
        run.AssertCompleted("node-b");
        run.AssertCompleted("node-sequence");
        run.AssertWorkflowCompleted();
    }

    [Fact]
    public async Task FaultedBranchOfADeferredParallel_IsRescheduled_AndTheJoinCompletes()
    {
        Assert.True(typeof(IRuntimeAcceptsRescheduledFaultedChild).IsAssignableFrom(typeof(ParallelActivity)));
        // One of two branches is enough, so the join defers on the faulted branch while the other one is suspended.
        await using var fixture = await RescheduleFixture.StartAsync(
            NewParallelNode(threshold: 1,
                ("a", InterventionTestSupport.NewLeafNode("node-f", typeof(OutcomeUnknownActivity))),
                ("b", InterventionTestSupport.NewLeafNode("node-b", typeof(SuspendOnceActivity)))),
            "actexec-parallel", "actexec-f", "actexec-b");
        var source = fixture.Run.State("node-f");
        Assert.Equal(ActivityExecutionStatus.Faulted, source.Status);
        Assert.Equal(ActivityExecutionStatus.Suspended, fixture.Run.State("node-b").Status);
        Assert.Equal(ActivityExecutionStatus.Running, fixture.Run.State("node-parallel").Status);

        var job = await fixture.RescheduleAsync(source.Execution.ActivityExecutionId, "reschedule-parallel-branch");

        Assert.Equal(WorkflowAlterationJobStatus.Succeeded, job.Status);
        var run = await fixture.ReadRunAsync();
        Assert.Single(run.States("node-f"), state => state.Status == ActivityExecutionStatus.Completed);
        run.AssertCompleted("node-parallel");
    }

    // The executable compiler sets the capability from the activity type; the harness builds nodes by hand.
    private static ExecutableNode NewSequenceNode(params ExecutableNode[] children) =>
        new(
            executableNodeId: "node-sequence",
            authoredActivityId: "authored-sequence",
            activityType: typeof(SequenceActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: typeof(SequenceDescriptor).FullName!,
            descriptorPayload: JsonSerializer.SerializeToElement(new SequenceDescriptor()),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            metadata: new Dictionary<string, string> { [RuntimeMetadataKeys.AcceptsRescheduledFaultedChild] = "true" },
            childSlots: [new ExecutableChildSlot(SequenceActivity.ActivitiesSlotName, children)],
            structure: new ExecutableActivityStructure(
                SequenceActivity.StructureKind,
                SequenceActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new { activities = children.Select(child => child.ExecutableNodeId).ToArray() })));

    private static ExecutableNode NewParallelNode(int threshold, params (string Name, ExecutableNode Activity)[] branches) =>
        new(
            executableNodeId: "node-parallel",
            authoredActivityId: "authored-parallel",
            activityType: typeof(ParallelActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: typeof(ParallelDescriptor).FullName!,
            descriptorPayload: JsonSerializer.SerializeToElement(new ParallelDescriptor()),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            metadata: new Dictionary<string, string> { [RuntimeMetadataKeys.AcceptsRescheduledFaultedChild] = "true" },
            childSlots: branches.Select(branch => new ExecutableChildSlot(ParallelActivity.BranchSlotName(branch.Name), [branch.Activity])).ToArray(),
            structure: new ExecutableActivityStructure(
                ParallelActivity.StructureKind,
                ParallelActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new
                {
                    branches = branches.Select(branch => new { name = branch.Name, activity = branch.Activity.ExecutableNodeId }).ToArray(),
                    threshold
                })));

    private sealed record SequenceDescriptor;
    private sealed record ParallelDescriptor;

    /// <summary>
    /// Schedules its slot children with a marker in their scheduling metadata; on a child fault it records the fault and
    /// defers without handling it. It records each child completion and, like a parent that routes on its children's
    /// command metadata, completes only on a completion that carries a marker.
    /// </summary>
    public sealed class RoutingParentActivity(ChildEvaluationLog log) : StructuralActivity,
        IRuntimeStructuralActivity,
        IRuntimeActivityChildCompletionHandler,
        IRuntimeActivityChildFaultHandler
    {
        public ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext context)
        {
            foreach (var child in Assert.Single(context.ExecutableNode.ChildSlots).Activities)
                context.ScheduleChildActivity(
                    child.ExecutableNodeId,
                    context.ActivityExecutionState.InvocationId,
                    new Dictionary<string, string> { [InterventionParentActivity.MarkerKey] = $"marker-{child.ExecutableNodeId}" });
            return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
        }

        public ValueTask<RuntimeStructuralContinuation> OnChildFaultedAsync(ActivityChildFaultedContext context)
        {
            log.Add(new ChildFaultEvaluation(
                context.FaultedChildActivityExecutionId,
                context.FaultedChildExecutableNodeId,
                context.IncidentId,
                context.FaultCode,
                context.FaultCategory));
            return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
        }

        public ValueTask<RuntimeStructuralContinuation> OnChildCompletedAsync(ActivityChildCompletedContext context)
        {
            var marker = ((IRuntimeActivityExecutionContext)context.ParentContext).SchedulerWorkItem.CommandMetadata
                .GetValueOrDefault(InterventionParentActivity.MarkerKey);
            log.Add(new ChildCompletionEvaluation(context.CompletedChildActivityExecutionId, context.CompletedChildExecutableNodeId, marker));
            return ValueTask.FromResult(marker is null
                ? RuntimeStructuralContinuation.Defer
                : RuntimeStructuralContinuation.Complete());
        }
    }

    /// <summary>How many more times a <see cref="SuspendOnceActivity"/> suspends before it completes.</summary>
    public sealed class SuspensionBudget(int suspensions)
    {
        public int Remaining { get; private set; } = suspensions;
        public bool TakeSuspension()
        {
            if (Remaining <= 0)
                return false;
            Remaining--;
            return true;
        }
    }

    /// <summary>Suspends on a trigger while its <see cref="SuspensionBudget"/> lasts, then completes when executed again.</summary>
    public sealed class SuspendOnceActivity(SuspensionBudget budget) : StatefulActivity<ActivityUnit, WaitState, WaitTrigger>
    {
        protected override ValueTask<ActivityTransition<ActivityUnit, WaitState>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(budget.TakeSuspension()
                ? Suspend(new WaitState("waiting"), [new ActivityTriggerRegistration<WaitTrigger>(WaitingActivity.ResumeTargetKey, "TestWait", "wait:42")])
                : Complete(ActivityUnit.Value, ActivityOutcomes.Done));

        protected override ValueTask<ActivityTransition<ActivityUnit, WaitState>> ResumeAsync(ActivityResumeContext<WaitState, WaitTrigger> context) =>
            ValueTask.FromResult(Complete(ActivityUnit.Value, ActivityOutcomes.Done));
    }

    /// <summary>
    /// One workflow under test: a routing structural parent over a leaf that faults or suspends once, plus the operator-side
    /// plan submission and orchestration that a host's alteration pump and resumption sweep perform.
    /// </summary>
    private sealed class RescheduleFixture : IAsyncDisposable
    {
        private readonly WorkflowExecutionHarness _harness;

        private RescheduleFixture(WorkflowExecutionHarness harness, ChildEvaluationLog log, WorkflowExecutionRun run)
        {
            _harness = harness;
            Log = log;
            Run = run;
        }

        public ChildEvaluationLog Log { get; }
        public WorkflowExecutionRun Run { get; }

        public static Task<RescheduleFixture> StartAsync(Type leafType) =>
            StartAsync(
                StructuralExecutionTestSupport.NewStructuralNode("node-parent", typeof(RoutingParentActivity),
                    InterventionTestSupport.NewLeafNode("node-f", leafType)),
                "actexec-parent", "actexec-f");

        public static async Task<RescheduleFixture> StartAsync(ExecutableNode root, params string[] activityExecutionIds)
        {
            var log = new ChildEvaluationLog();
            var harness = WorkflowExecutionHarness.Create()
                .ConfigureServices(services =>
                {
                    services.AddSingleton(log);
                    services.AddSingleton(new SuspensionBudget(1));
                    services.AddSingleton(new OperationKeyLog());
                    services.AddSingleton(new FailureBudget(1));
                    services.TryAddScoped<IRuntimeResumptionService, RuntimeResumptionService>();
                })
                .Build(activityExecutionIds);
            var executable = StructuralExecutionTestSupport.NewExecutable(
                root,
                waitingNodeIds: ["node-f", "node-b"],
                incidentStrategy: IncidentStrategyBuiltIns.ContinueWithIncidentsReference);
            var reference = await harness.PublishAsync(executable, "reschedule-source");
            await harness.StartPublishedAsync(reference, harness.ExecutionId, tenantId: WorkflowExecutionHarness.Partition.Value);
            return new RescheduleFixture(harness, log, await harness.ReadRunAsync(harness.ExecutionId));
        }

        public async Task<WorkflowAlterationJobState> RescheduleAsync(string sourceActivityExecutionId, string idempotencyKey)
        {
            WorkflowAlterationPlanAdmissionResult admission;
            await using (var scope = _harness.Services.CreateAsyncScope())
            {
                admission = await scope.ServiceProvider.GetRequiredService<WorkflowAlterationPlanService>().SubmitAsync(
                    new WorkflowAlterationSubmission(
                        Authority,
                        WorkflowAlterationTargetSelector.ForExecutionIds([_harness.ExecutionId]),
                        [new WorkflowAlterationEnvelope("RescheduleActivity", 1, JsonSerializer.SerializeToElement(new { sourceActivityExecutionId }))]),
                    new WorkflowAlterationOperatorProvenance("operator-1", correlationId: null),
                    idempotencyKey);
            }

            var jobId = WorkflowAlterationIdentity.CreateJobId(admission.Plan.PlanId, _harness.ExecutionId);
            var options = new WorkflowAlterationOrchestrationOptions { WorkerId = "test-worker" };
            WorkflowAlterationJobState? job = null;
            for (var sweep = 0; sweep < 8; sweep++)
            {
                await using (var scope = _harness.Services.CreateAsyncScope())
                {
                    await scope.ServiceProvider.GetRequiredService<WorkflowAlterationOrchestrationSweep>()
                        .ExecuteAsync(options, "test-worker");
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
