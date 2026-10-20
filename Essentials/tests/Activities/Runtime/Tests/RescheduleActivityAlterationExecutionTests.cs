using System.Text.Json;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
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

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// <c>RescheduleActivity/1</c> end to end. A structural parent that deferred on a child fault it did not handle, under
/// the <c>ContinueWithIncidents</c> strategy, keeps the run running and keeps waiting for that child; rescheduling the
/// faulted child through a submitted alteration plan lets the parent receive and route the successor's completion. A
/// fault the parent absorbed is a consumed outcome, so rescheduling its source is refused.
/// </summary>
public sealed class RescheduleActivityAlterationExecutionTests
{
    private static readonly WorkflowAlterationAuthorityScope Authority =
        new(WorkflowExecutionHarness.Partition.Value, "activity-behavioural-drive", "activity-behavioural-drive");

    [Fact]
    public async Task RescheduledLeafWhoseFaultTheParentLeftOpen_Completes_AndTheParentRoutesItsCompletion()
    {
        await using var fixture = await RescheduleFixture.StartAsync(absorbFault: false);
        var source = fixture.Run.State("node-f");
        Assert.Equal(ActivityExecutionStatus.Faulted, source.Status);
        Assert.True(source.Metadata.ContainsKey(RuntimeMetadataKeys.ParentCompletionProcessedWorkItemId));
        Assert.Equal(WorkflowExecutionStatus.Running, fixture.Run.WorkflowState!.Status);
        Assert.Equal(IncidentStatus.Open, Assert.Single(await fixture.ListIncidentsAsync()).Status);

        var job = await fixture.RescheduleAsync(source.Execution.ActivityExecutionId, "reschedule-1");

        Assert.Equal(WorkflowAlterationJobStatus.Succeeded, job.Status);
        var run = await fixture.ReadRunAsync();
        var successor = Assert.Single(run.States("node-f"), state => state.Status == ActivityExecutionStatus.Completed);
        Assert.Equal(source.Execution.ActivityExecutionId, successor.Metadata[RuntimeMetadataKeys.SupersedesActivityExecutionId]);

        var fault = Assert.Single(fixture.Log.Faults);
        Assert.Equal(source.Execution.ActivityExecutionId, fault.ChildActivityExecutionId);
        var completion = Assert.Single(fixture.Log.Completions);
        Assert.Equal(successor.Execution.ActivityExecutionId, completion.ChildActivityExecutionId);
        // The parent routes the successor's completion with the scheduling metadata it gave the source.
        Assert.Equal("marker-node-f", successor.Provenance.Metadata[InterventionParentActivity.MarkerKey]);
        Assert.Equal("marker-node-f", completion.Marker);
        run.AssertCompleted("node-parent");
        run.AssertWorkflowCompleted();

        // The successor is a fresh invocation: none of the source's per-invocation markers or fault metadata carry over.
        Assert.DoesNotContain(successor.Metadata.Keys, key => key.StartsWith("runtime.fault", StringComparison.Ordinal));
        Assert.DoesNotContain(RuntimeMetadataKeys.IncidentId, successor.Metadata.Keys);
    }

    [Fact]
    public async Task LeafWhoseFaultTheParentAbsorbed_IsRejectedSourceFaultAbsorbed()
    {
        await using var fixture = await RescheduleFixture.StartAsync(absorbFault: true);
        var source = fixture.Run.State("node-f");
        Assert.Equal(ActivityExecutionStatus.Faulted, source.Status);
        Assert.Equal(IncidentStatus.Resolved, Assert.Single(await fixture.ListIncidentsAsync()).Status);

        var job = await fixture.RescheduleAsync(source.Execution.ActivityExecutionId, "reschedule-absorbed");

        Assert.Equal(WorkflowAlterationJobStatus.Failed, job.Status);
        Assert.Equal("SourceFaultAbsorbed", Assert.Single(job.Outcomes).Code);
        var run = await fixture.ReadRunAsync();
        Assert.Equal(ActivityExecutionStatus.Faulted, Assert.Single(run.States("node-f")).Status);
        Assert.Equal(WorkflowExecutionStatus.Running, run.WorkflowState!.Status);
        Assert.Empty(fixture.Log.Completions);
    }

    /// <summary>What the <see cref="RoutingParentActivity"/> does with a child fault.</summary>
    public sealed record RescheduleParentDirective(bool AbsorbFault);

    /// <summary>
    /// Schedules its slot children with a marker in their scheduling metadata; on a child fault it records the fault and
    /// defers, absorbing it when directed. It records each child completion and, like a parent that routes on its
    /// children's command metadata, completes only on a completion that carries a marker.
    /// </summary>
    public sealed class RoutingParentActivity(ChildEvaluationLog log, RescheduleParentDirective directive) : StructuralActivity,
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
            if (directive.AbsorbFault)
                ((IRuntimeActivityExecutionContext)context.ParentContext).RequestChildFaultAbsorption(context.IncidentId!, "test-absorb");
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

    /// <summary>
    /// One workflow under test: a routing structural parent over a leaf that faults once, plus the operator-side
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

        public static async Task<RescheduleFixture> StartAsync(bool absorbFault)
        {
            var log = new ChildEvaluationLog();
            var harness = WorkflowExecutionHarness.Create()
                .ConfigureServices(services =>
                {
                    services.AddSingleton(log);
                    services.AddSingleton(new RescheduleParentDirective(absorbFault));
                    services.AddSingleton(new OperationKeyLog());
                    services.AddSingleton(new FailureBudget(1));
                    services.TryAddScoped<IRuntimeResumptionService, RuntimeResumptionService>();
                })
                .Build("actexec-parent", "actexec-f");
            var executable = StructuralExecutionTestSupport.NewExecutable(
                StructuralExecutionTestSupport.NewStructuralNode("node-parent", typeof(RoutingParentActivity),
                    InterventionTestSupport.NewLeafNode("node-f", typeof(OutcomeUnknownActivity))),
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
