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
/// <c>RescheduleActivity/1</c> end to end: a faulted leaf whose fault its structural parent already absorbed (the
/// incident is resolved, the parent deferred and keeps running) is rescheduled through a submitted alteration plan, and
/// the parent receives the successor's completion as an ordinary child completion.
/// </summary>
public sealed class RescheduleActivityAlterationExecutionTests
{
    private static readonly WorkflowAlterationAuthorityScope Authority =
        new(WorkflowExecutionHarness.Partition.Value, "activity-behavioural-drive", "activity-behavioural-drive");

    [Fact]
    public async Task RescheduledLeafWithAnAbsorbedFault_Completes_AndTheParentSeesItsCompletion()
    {
        await using var fixture = await RescheduleFixture.StartAsync();
        var source = fixture.Run.State("node-f");
        Assert.Equal(ActivityExecutionStatus.Faulted, source.Status);
        Assert.True(source.Metadata.ContainsKey(RuntimeMetadataKeys.ParentCompletionProcessedWorkItemId));
        Assert.Equal(WorkflowExecutionStatus.Running, fixture.Run.WorkflowState!.Status);
        Assert.Equal(IncidentStatus.Resolved, Assert.Single(await fixture.ListIncidentsAsync()).Status);

        var job = await fixture.RescheduleAsync(source.Execution.ActivityExecutionId, "reschedule-1");

        Assert.Equal(WorkflowAlterationJobStatus.Succeeded, job.Status);
        var run = await fixture.ReadRunAsync();
        var successor = Assert.Single(run.States("node-f"), state => state.Status == ActivityExecutionStatus.Completed);
        Assert.Equal(source.Execution.ActivityExecutionId, successor.Metadata[RuntimeMetadataKeys.SupersedesActivityExecutionId]);

        var fault = Assert.Single(fixture.Log.Faults);
        Assert.Equal(source.Execution.ActivityExecutionId, fault.ChildActivityExecutionId);
        var completion = Assert.Single(fixture.Log.Completions);
        Assert.Equal(successor.Execution.ActivityExecutionId, completion.ChildActivityExecutionId);
        run.AssertCompleted("node-parent");
        run.AssertWorkflowCompleted();

        // The successor is a fresh invocation: none of the source's per-invocation markers or fault metadata carry over.
        Assert.DoesNotContain(successor.Metadata.Keys, key => key.StartsWith("runtime.fault", StringComparison.Ordinal));
        Assert.DoesNotContain(RuntimeMetadataKeys.IncidentId, successor.Metadata.Keys);
    }

    /// <summary>
    /// Schedules its slot children; absorbs a child fault and defers, so the run keeps running with the faulted child's
    /// incident resolved; records each child outcome it is told about and completes on a child completion.
    /// </summary>
    public sealed class AbsorbingParentActivity(ChildEvaluationLog log) : StructuralActivity,
        IRuntimeStructuralActivity,
        IRuntimeActivityChildCompletionHandler,
        IRuntimeActivityChildFaultHandler
    {
        public ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext context)
        {
            foreach (var child in Assert.Single(context.ExecutableNode.ChildSlots).Activities)
                context.ScheduleChildActivity(child.ExecutableNodeId, context.ActivityExecutionState.InvocationId);
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
            ((IRuntimeActivityExecutionContext)context.ParentContext).RequestChildFaultAbsorption(context.IncidentId!, "test-absorb");
            return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
        }

        public ValueTask<RuntimeStructuralContinuation> OnChildCompletedAsync(ActivityChildCompletedContext context)
        {
            log.Add(new ChildCompletionEvaluation(context.CompletedChildActivityExecutionId, context.CompletedChildExecutableNodeId));
            return ValueTask.FromResult(RuntimeStructuralContinuation.Complete());
        }
    }

    /// <summary>
    /// One workflow under test: an absorbing structural parent over a leaf that faults once, plus the operator-side
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

        public static async Task<RescheduleFixture> StartAsync()
        {
            var log = new ChildEvaluationLog();
            var harness = WorkflowExecutionHarness.Create()
                .ConfigureServices(services =>
                {
                    services.AddSingleton(log);
                    services.AddSingleton(new OperationKeyLog());
                    services.AddSingleton(new FailureBudget(1));
                    services.TryAddScoped<IRuntimeResumptionService, RuntimeResumptionService>();
                })
                .Build("actexec-parent", "actexec-f");
            var executable = StructuralExecutionTestSupport.NewExecutable(
                StructuralExecutionTestSupport.NewStructuralNode("node-parent", typeof(AbsorbingParentActivity),
                    InterventionTestSupport.NewLeafNode("node-f", typeof(OutcomeUnknownActivity))));
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
