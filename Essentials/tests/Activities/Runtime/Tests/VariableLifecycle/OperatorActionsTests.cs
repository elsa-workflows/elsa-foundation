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
            var reads = new CountReadLog();
            var harness = WorkflowExecutionHarness.Create()
                .ConfigureServices(services =>
                {
                    // The parent assigns no operation keys, so each invocation derives its own (a parent-assigned key stays the parent's).
                    services.AddSingleton(new InterventionDirective { AssignKeys = false });
                    services.AddSingleton(new ChildEvaluationLog());
                    services.AddSingleton(new OperationKeyLog());
                    services.AddSingleton(new FailureBudget(1));
                    services.AddSingleton(reads);
                    services.TryAddScoped<IRuntimeResumptionService, RuntimeResumptionService>();
                })
                .Build("actexec-parent", "actexec-f");
            var executable = WorkflowExecutionHarness.NewExecutable(
                StructuralExecutionTestSupport.NewStructuralNode("node-parent", typeof(InterventionParentActivity),
                    VariableLifecycleTestGraph.Increment("node-f", typeof(FlakyIncrementActivity))),
                VariableLifecycleTestGraph.CountDeclaration(initial: 5));
            var reference = await harness.PublishAsync(executable, "operator-actions");
            await harness.StartPublishedAsync(reference, harness.ExecutionId, tenantId: WorkflowExecutionHarness.Partition.Value);
            var run = await harness.ReadRunAsync(harness.ExecutionId);
            Assert.Equal(ActivityExecutionStatus.Faulted, run.State("node-f").Status);
            return new Fixture(harness, reads, run);
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

/// <summary>What each run of the flaky leaf saw.</summary>
public sealed class CountReadLog
{
    private readonly List<(int Count, string OperationKey)> _observed = [];
    public IReadOnlyList<(int Count, string OperationKey)> Observed { get { lock (_observed) return _observed.ToArray(); } }
    internal void Add(int count, string operationKey) { lock (_observed) _observed.Add((count, operationKey)); }
}

/// <summary>An External leaf that reads <c>Count</c>, faults while its budget lasts, and otherwise returns <c>Count + 1</c>.</summary>
public sealed class FlakyIncrementActivity(FailureBudget budget, CountReadLog reads) : Activity<Incremented>
{
    [ActivityInput]
    public int Count { get; set; }

    protected override ValueTask<ActivityTransition<Incremented>> ExecuteAsync(ActivityExecutionContext context)
    {
        reads.Add(Count, context.OperationKey);
        return ValueTask.FromResult(budget.TakeFailure()
            ? ActivityTransition.Fault<Incremented>(new ActivityFault(OutcomeUnknownActivity.Code, "The response was lost.", category: OutcomeUnknownActivity.Category))
            : ActivityTransition.Complete(new Incremented(Count + 1), ActivityOutcomes.Done));
    }
}
