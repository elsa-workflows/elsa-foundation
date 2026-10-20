using System.Text.Json;
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
/// <c>RetryFaultedActivity/1</c> end to end: a faulted leaf under a structural parent that kept its incident waiting
/// for intervention is retried through a submitted alteration plan, the successor runs with the source's operation
/// key, and the parent receives it as an ordinary child completion.
/// </summary>
public sealed class RetryFaultedActivityAlterationExecutionTests
{
    private const string Operator = "operator-1";
    private static readonly WorkflowAlterationAuthorityScope Authority =
        new(WorkflowExecutionHarness.Partition.Value, "activity-behavioural-drive", "activity-behavioural-drive");

    [Fact]
    public async Task RetriedLeaf_Completes_AndTheParentSeesAnOrdinaryChildCompletion()
    {
        await using var fixture = await RetryFixture.StartAsync(new InterventionDirective());
        var source = fixture.Run.State("node-f");
        Assert.Equal(ActivityExecutionStatus.Faulted, source.Status);
        Assert.Equal(WorkflowExecutionStatus.Running, fixture.Run.WorkflowState!.Status);

        var job = await fixture.RetryAsync(source.Execution.ActivityExecutionId, "retry-1", "Delivery service restored");

        Assert.Equal(WorkflowAlterationJobStatus.Succeeded, job.Status);
        var run = await fixture.ReadRunAsync();
        run.AssertWorkflowCompleted();
        run.AssertCompleted("node-parent");
        var states = run.States("node-f");
        Assert.Equal(2, states.Count);
        var superseded = Assert.Single(states, state => state.Status == ActivityExecutionStatus.Superseded);
        var successor = Assert.Single(states, state => state.Status == ActivityExecutionStatus.Completed);
        Assert.Equal(source.Execution.ActivityExecutionId, superseded.Execution.ActivityExecutionId);
        Assert.Equal(successor.Execution.ActivityExecutionId, superseded.SupersededByActivityExecutionId);

        // The successor keeps the source's parent, scheduling identity, scheduling cause and scheduling metadata.
        Assert.Equal(source.ParentActivityExecutionId, successor.ParentActivityExecutionId);
        Assert.Equal(source.SchedulingActivityExecutionId, successor.SchedulingActivityExecutionId);
        Assert.Equal(source.Provenance.SchedulingCause, successor.Provenance.SchedulingCause);
        Assert.Equal("marker-node-f", successor.Provenance.Metadata[InterventionParentActivity.MarkerKey]);
        Assert.Equal(source.Execution.ActivityExecutionId, successor.Metadata[RuntimeMetadataKeys.RetryOfActivityExecutionId]);
        Assert.Equal(source.Execution.ActivityExecutionId, successor.Metadata[RuntimeMetadataKeys.SupersedesActivityExecutionId]);

        // The parent was told about the source's fault once, and about the successor's completion as an ordinary
        // completion. Superseding the faulted source reports nothing to the parent.
        var fault = Assert.Single(fixture.Log.Faults);
        Assert.Equal(source.Execution.ActivityExecutionId, fault.ChildActivityExecutionId);
        var completion = Assert.Single(fixture.Log.Completions);
        Assert.Equal(successor.Execution.ActivityExecutionId, completion.ChildActivityExecutionId);
        Assert.Equal("node-f", completion.ChildExecutableNodeId);

        var incident = Assert.Single(await fixture.ListIncidentsAsync());
        Assert.Equal(IncidentStatus.Resolved, incident.Status);
        Assert.NotNull(incident.ResolvedAt);
        var outcome = incident.ResolutionOutcome!;
        Assert.Equal(IncidentResolutionActionKinds.OperatorRetry, outcome.ActionKind);
        Assert.Equal(IncidentResolutionSystemSources.OperatorRetryAlteration, outcome.SystemSource);
        Assert.Equal(job.JobId, outcome.Metadata["runtime.alteration.jobId"]);
        Assert.Equal(job.PlanId, outcome.Metadata["runtime.alteration.planId"]);
        Assert.Equal(Operator, outcome.Metadata["runtime.alteration.submittedBy"]);
        Assert.Equal("Delivery service restored", outcome.Metadata["runtime.alteration.reason"]);
        Assert.Equal(IncidentResolutionActionKinds.WaitForIntervention, incident.Metadata[RuntimeMetadataKeys.PriorResolutionActionKind]);
        Assert.Equal(IncidentResolutionSystemSources.StructuralIntervention, incident.Metadata[RuntimeMetadataKeys.PriorResolutionSystemSource]);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RetriedLeaf_RunsWithTheSourcesOperationKey(bool assignKeys)
    {
        await using var fixture = await RetryFixture.StartAsync(new InterventionDirective(), assignKeys);
        var source = fixture.Run.State("node-f");

        await fixture.RetryAsync(source.Execution.ActivityExecutionId, "retry-keys");

        Assert.Equal(2, fixture.Keys.Observed.Count);
        var first = fixture.Keys.Observed[0];
        var retried = fixture.Keys.Observed[1];
        Assert.NotEqual(first.InvocationId, retried.InvocationId);
        Assert.Equal(assignKeys ? "op/node-f" : source.Execution.ActivityExecutionId, first.OperationKey);
        Assert.Equal(first.OperationKey, retried.OperationKey);
        Assert.Equal(first.TargetOperationKey, retried.TargetOperationKey);
    }

    [Fact]
    public async Task SameIdempotencyKeyTwice_SchedulesOneSuccessor()
    {
        await using var fixture = await RetryFixture.StartAsync(new InterventionDirective());
        var sourceId = fixture.Run.State("node-f").Execution.ActivityExecutionId;

        var first = await fixture.SubmitAsync(sourceId, "retry-same");
        var replay = await fixture.SubmitAsync(sourceId, "retry-same");
        await fixture.OrchestrateAsync(first.Plan.PlanId);

        Assert.False(first.IsReplay);
        Assert.True(replay.IsReplay);
        Assert.Equal(first.Plan.PlanId, replay.Plan.PlanId);
        var run = await fixture.ReadRunAsync();
        run.AssertWorkflowCompleted();
        Assert.Equal(2, run.States("node-f").Count);
        Assert.Equal(2, fixture.Keys.Observed.Count);
    }

    [Fact]
    public async Task NewIdempotencyKeyAfterSuccess_IsRejectedSourceActivityNotFaulted()
    {
        await using var fixture = await RetryFixture.StartAsync(new InterventionDirective { KeepParentRunning = true });
        var sourceId = fixture.Run.State("node-f").Execution.ActivityExecutionId;
        Assert.Equal(WorkflowAlterationJobStatus.Succeeded, (await fixture.RetryAsync(sourceId, "retry-first")).Status);

        var second = await fixture.RetryAsync(sourceId, "retry-second");

        Assert.Equal(WorkflowAlterationJobStatus.Failed, second.Status);
        Assert.Equal("SourceActivityNotFaulted", Assert.Single(second.Outcomes).Code);
        Assert.Equal(2, (await fixture.ReadRunAsync()).States("node-f").Count);
    }

    [Fact]
    public async Task TerminalRun_IsRejectedWorkflowTerminal()
    {
        // A parent that neither absorbs nor requests intervention leaves the incident to the default Fault strategy.
        await using var fixture = await RetryFixture.StartAsync(new InterventionDirective(), parentType: typeof(PassthroughStructuralActivity));
        Assert.Equal(WorkflowExecutionStatus.Faulted, fixture.Run.WorkflowState!.Status);

        var job = await fixture.RetryAsync(fixture.Run.State("node-f").Execution.ActivityExecutionId, "retry-terminal");

        Assert.Equal(WorkflowAlterationJobStatus.Failed, job.Status);
        Assert.Equal("WorkflowTerminal", Assert.Single(job.Outcomes).Code);
        Assert.Single((await fixture.ReadRunAsync()).States("node-f"));
    }

    [Fact]
    public async Task RetriedLeafFaultingAgain_WaitsForInterventionWithAFreshIncident()
    {
        await using var fixture = await RetryFixture.StartAsync(new InterventionDirective(), failures: 2);
        var sourceId = fixture.Run.State("node-f").Execution.ActivityExecutionId;

        Assert.Equal(WorkflowAlterationJobStatus.Succeeded, (await fixture.RetryAsync(sourceId, "retry-again")).Status);

        var run = await fixture.ReadRunAsync();
        Assert.Equal(WorkflowExecutionStatus.Running, run.WorkflowState!.Status);
        var successor = Assert.Single(run.States("node-f"), state => state.Status == ActivityExecutionStatus.Faulted);
        Assert.Equal(2, fixture.Log.Faults.Count);
        Assert.Equal(successor.Execution.ActivityExecutionId, fixture.Log.Faults[1].ChildActivityExecutionId);
        var incidents = await fixture.ListIncidentsAsync();
        Assert.Equal(2, incidents.Count);
        Assert.Single(incidents, incident => incident.Status == IncidentStatus.Resolved);
        var waiting = Assert.Single(incidents, incident => incident.Status == IncidentStatus.Blocking);
        Assert.Equal(successor.Execution.ActivityExecutionId, waiting.ActivityExecutionId);
        Assert.Equal(IncidentResolutionActionKinds.WaitForIntervention, waiting.ResolutionOutcome!.ActionKind);
    }

    /// <summary>
    /// One workflow under test: a structural parent over a leaf whose outcome is unknown for its first attempts, plus the
    /// operator-side plan submission and orchestration that a host's alteration pump and resumption sweep perform.
    /// </summary>
    private sealed class RetryFixture : IAsyncDisposable
    {
        private readonly WorkflowExecutionHarness _harness;

        private RetryFixture(WorkflowExecutionHarness harness, ChildEvaluationLog log, OperationKeyLog keys, WorkflowExecutionRun run)
        {
            _harness = harness;
            Log = log;
            Keys = keys;
            Run = run;
        }

        public ChildEvaluationLog Log { get; }
        public OperationKeyLog Keys { get; }
        public WorkflowExecutionRun Run { get; }

        public static async Task<RetryFixture> StartAsync(
            InterventionDirective directive,
            bool assignKeys = true,
            Type? parentType = null,
            int failures = 1)
        {
            var log = new ChildEvaluationLog();
            var keys = new OperationKeyLog();
            var harness = WorkflowExecutionHarness.Create()
                .ConfigureServices(services =>
                {
                    services.AddSingleton(directive with { AssignKeys = assignKeys });
                    services.AddSingleton(log);
                    services.AddSingleton(keys);
                    services.AddSingleton(new FailureBudget(failures));
                    services.TryAddScoped<IRuntimeResumptionService, RuntimeResumptionService>();
                })
                .Build("actexec-parent", "actexec-f");
            var executable = StructuralExecutionTestSupport.NewExecutable(
                StructuralExecutionTestSupport.NewStructuralNode("node-parent", parentType ?? typeof(InterventionParentActivity),
                    InterventionTestSupport.NewLeafNode("node-f", typeof(OutcomeUnknownActivity))));
            var reference = await harness.PublishAsync(executable, "retry-source");
            await harness.StartPublishedAsync(reference, harness.ExecutionId, tenantId: WorkflowExecutionHarness.Partition.Value);
            return new RetryFixture(harness, log, keys, await harness.ReadRunAsync(harness.ExecutionId));
        }

        public async Task<WorkflowAlterationJobState> RetryAsync(string sourceActivityExecutionId, string idempotencyKey, string? reason = null)
        {
            var admission = await SubmitAsync(sourceActivityExecutionId, idempotencyKey, reason);
            return await OrchestrateAsync(admission.Plan.PlanId);
        }

        public async Task<WorkflowAlterationPlanAdmissionResult> SubmitAsync(string sourceActivityExecutionId, string idempotencyKey, string? reason = null)
        {
            await using var scope = _harness.Services.CreateAsyncScope();
            var payload = reason is null
                ? JsonSerializer.SerializeToElement(new { sourceActivityExecutionId })
                : JsonSerializer.SerializeToElement(new { sourceActivityExecutionId, reason });
            return await scope.ServiceProvider.GetRequiredService<WorkflowAlterationPlanService>().SubmitAsync(
                new WorkflowAlterationSubmission(
                    Authority,
                    WorkflowAlterationTargetSelector.ForExecutionIds([_harness.ExecutionId]),
                    [new WorkflowAlterationEnvelope(RetryFaultedActivityAlterationHandler.Kind, RetryFaultedActivityAlterationHandler.SchemaVersion, payload)]),
                new WorkflowAlterationOperatorProvenance(Operator, correlationId: null),
                idempotencyKey);
        }

        /// <summary>Runs the alteration pump's sweeps until the job is terminal, then the resumption sweeps that deliver its continuation.</summary>
        public async Task<WorkflowAlterationJobState> OrchestrateAsync(string planId)
        {
            var jobId = WorkflowAlterationIdentity.CreateJobId(planId, _harness.ExecutionId);
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
