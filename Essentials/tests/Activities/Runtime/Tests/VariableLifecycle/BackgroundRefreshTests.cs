using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Runtime.Services;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Elsa.Workflows.Runtime.Services.Alterations;
using Elsa.Workflows.Runtime.Services.Alterations.Handlers;
using System.Text.Json;
using Elsa.Workflows.Runtime.Services.Values;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests.VariableLifecycle;

/// <summary>
/// US5 (spec 1221 T029 to T032, T010a; FR-018 to FR-020): a background step runs on fresh inputs. An inline leaf and a
/// background leaf both read <c>count = 0</c> at Start (fusion off, so neither commits in between). The inline leaf
/// commits <c>1</c> before the background leaf is admitted. Admission sees <c>count</c> moved from v0 to v1 and reads
/// the inputs again as a second evaluation of the same invocation, so the worker adds to <c>1</c>. A node that turned
/// the refresh off runs on the <c>0</c> it read and meets the commit rule: a warning by default, a fault under
/// <c>Fault</c>.
/// </summary>
public sealed class BackgroundRefreshTests
{
    private static readonly string[] Ids = ["actexec-parallel", "actexec-left", "actexec-right"];

    [Fact]
    public async Task A_variable_changed_before_admission_is_read_again_as_a_new_evaluation()
    {
        await using var harness = NewHarness();

        var run = await RunAsync(harness, Background());

        Assert.Equal(2, VariableLifecycleTestGraph.ReadCount(run));
        var background = run.State("node-right");
        Assert.Equal(ActivityExecutionStatus.Completed, background.Status);
        // The worker received the second evaluation, which read count at v1; the first stays in the history with the reason.
        var snapshot = background.InputSnapshot!;
        Assert.Equal(2, snapshot.EvaluationNumber);
        Assert.Equal(1, Assert.Single(snapshot.InputReadSet!.Entries).Version);
        var refresh = Assert.Single(background.InputRefreshes!);
        Assert.Equal(2, refresh.ReplacedByEvaluation);
        Assert.Equal(0, Assert.Single(refresh.Superseded.InputReadSet!.Entries).Version);
        Assert.Equal(0, refresh.Superseded.Values[nameof(BackgroundIncrementActivity.Count)].InlineValue!.Value.GetInt32());
        Assert.Equal($"{VariableLifecycleTestGraph.CountKey} v0 \u2192 v1", refresh.Reason);
        Assert.Equal(VariableCommitOutcome.Current, Assert.Single(background.VariableWrites!).Outcome);
        // Same invocation: the refresh is an evaluation, not a new row.
        Assert.Single(run.States("node-right"));
    }

    // T030 / FR-019, and T010a: refresh off, so the worker adds to the 0 it read at Start. Its write is on a stale base:
    // it applies with a lost-update warning that names both writers and keeps the overwritten value.
    [Fact]
    public async Task With_refresh_off_a_stale_base_warns_at_commit_by_default()
    {
        await using var harness = NewHarness();

        var run = await RunAsync(harness, Background(refreshOff: true));

        Assert.Equal(1, VariableLifecycleTestGraph.ReadCount(run));
        var background = run.State("node-right");
        Assert.Null(background.InputRefreshes);
        Assert.Equal(1, background.InputSnapshot!.EvaluationNumber);
        var record = Assert.Single(background.VariableWrites!);
        Assert.Equal(VariableCommitOutcome.LostUpdateWarning, record.Outcome);
        Assert.Equal(VariableWriteSource.BackgroundResult, record.Writer.Source);
        Assert.Equal("node-left", record.OverwrittenWriter!.ExecutableNodeId);
    }

    [Fact]
    public async Task With_refresh_off_a_stale_base_faults_under_Fault()
    {
        await using var harness = NewHarness();

        var run = await RunAsync(harness, Background(refreshOff: true, LostUpdateMode.Fault), allowFault: true);

        Assert.Equal(1, VariableLifecycleTestGraph.ReadCount(run));
        var background = run.State("node-right");
        Assert.Equal(ActivityExecutionStatus.Faulted, background.Status);
        Assert.Equal(VariableCommitOutcome.LostUpdateFault, Assert.Single(background.VariableWrites!).Outcome);
    }

    // T010a: two background branches are both admitted on count = 0, before either worker runs, so the refresh has
    // nothing to see (it covers only Start to admission). The second result is on a stale base: it applies with a
    // lost-update warning that names the first writer and keeps the overwritten value.
    [Fact]
    public async Task Two_background_branches_total_one_with_a_lost_update_warning_on_the_second_commit()
    {
        await using var harness = NewHarness();
        var executable = WorkflowExecutionHarness.NewExecutable(
            VariableLifecycleTestGraph.Parallel(
                VariableLifecycleTestGraph.Increment("node-left", typeof(BackgroundIncrementActivity), ActivityExecutionPlacement.Background),
                VariableLifecycleTestGraph.Increment("node-right", typeof(BackgroundIncrementActivity), ActivityExecutionPlacement.Background)),
            VariableLifecycleTestGraph.CountDeclaration());

        var run = await RunAsync(harness, executable);

        Assert.Equal(1, VariableLifecycleTestGraph.ReadCount(run));
        var records = new[] { run.State("node-left"), run.State("node-right") }
            .Select(state => Assert.Single(state.VariableWrites!))
            .ToArray();
        Assert.All(records, record => Assert.Equal(VariableWriteSource.BackgroundResult, record.Writer.Source));
        Assert.Single(records, record => record.Outcome == VariableCommitOutcome.Current);
        var warning = Assert.Single(records, record => record.Outcome == VariableCommitOutcome.LostUpdateWarning);
        Assert.NotEqual(warning.Writer.ExecutableNodeId, warning.OverwrittenWriter!.ExecutableNodeId);
        Assert.Null(run.State("node-left").InputRefreshes);
        Assert.Null(run.State("node-right").InputRefreshes);
    }

    // FR-014 / ADR 0092 decision 8: a retry keeps its committed inputs, in the background too. The background step read
    // count = 0 and faulted; the operator sets count to 5 and retries. The successor is not refreshed at admission: the
    // worker runs on the 0 it read, and its write on the stale base warns and keeps the operator's 5.
    [Fact]
    public async Task A_retried_background_step_keeps_its_committed_inputs()
    {
        var reads = new CountReadLog();
        await using var harness = NewHarness(services => services
            .AddSingleton(reads)
            .AddSingleton(new FailureBudget(1))
            .AddSingleton(new JoinDirective()));
        var executable = WorkflowExecutionHarness.NewExecutable(
            StructuralExecutionTestSupport.NewStructuralNode("node-parent", typeof(JoiningInterventionParentActivity),
                VariableLifecycleTestGraph.Increment("node-right", typeof(FlakyBackgroundIncrementActivity), ActivityExecutionPlacement.Background)),
            VariableLifecycleTestGraph.CountDeclaration());
        var run = await RunAsync(harness, executable, allowFault: true);
        var source = run.State("node-right");
        Assert.Equal(ActivityExecutionStatus.Faulted, source.Status);

        Assert.Equal(WorkflowAlterationJobStatus.Succeeded,
            (await SubmitAsync(harness, "ModifyVariable", new { variableKey = VariableLifecycleTestGraph.CountKey, value = 5 }, "modify-5")).Status);
        Assert.Equal(WorkflowAlterationJobStatus.Succeeded,
            (await SubmitAsync(harness, RetryFaultedActivityAlterationHandler.Kind, new { sourceActivityExecutionId = source.Execution.ActivityExecutionId }, "retry")).Status);
        run = await DrainWorkersAsync(harness);

        run.AssertWorkflowCompleted();
        Assert.Equal([0, 0], reads.Observed.Select(item => item.Count));
        var successor = Assert.Single(run.States("node-right"), state => state.Status == ActivityExecutionStatus.Completed);
        Assert.Null(successor.InputRefreshes);
        Assert.Equal(1, successor.InputSnapshot!.EvaluationNumber);
        var record = Assert.Single(successor.VariableWrites!);
        Assert.Equal(VariableCommitOutcome.LostUpdateWarning, record.Outcome);
        Assert.Equal(5, record.OverwrittenValue!.InlineValue!.Value.GetInt32());
    }

    // With nothing changed between Start and admission, the inputs Start read are the ones the worker receives.
    [Fact]
    public async Task Unchanged_inputs_are_not_refreshed()
    {
        await using var harness = NewHarness();
        var executable = WorkflowExecutionHarness.NewExecutable(
            VariableLifecycleTestGraph.Increment("node-right", typeof(BackgroundIncrementActivity), ActivityExecutionPlacement.Background),
            VariableLifecycleTestGraph.CountDeclaration());

        var run = await RunAsync(harness, executable);

        Assert.Equal(1, VariableLifecycleTestGraph.ReadCount(run));
        Assert.Null(run.State("node-right").InputRefreshes);
        Assert.Equal(1, run.State("node-right").InputSnapshot!.EvaluationNumber);
    }

    private static WorkflowExecutable Background(bool refreshOff = false, LostUpdateMode mode = LostUpdateMode.Default) =>
        WorkflowExecutionHarness.NewExecutable(
            VariableLifecycleTestGraph.Parallel(
                VariableLifecycleTestGraph.Increment("node-left", typeof(IncrementActivity)),
                VariableLifecycleTestGraph.Increment("node-right", typeof(BackgroundIncrementActivity), ActivityExecutionPlacement.Background, mode,
                    metadata: refreshOff
                        ? new Dictionary<string, string> { [RuntimeMetadataKeys.BackgroundInputRefresh] = RuntimeMetadataKeys.BackgroundInputRefreshOff }
                        : null)),
            VariableLifecycleTestGraph.CountDeclaration());

    private static WorkflowExecutionHarness NewHarness(Action<IServiceCollection>? configure = null) =>
        WorkflowExecutionHarness.Create()
            .WithFeature(services => new Elsa.Activities.ControlFlow.ActivitiesControlFlowFeature().ConfigureServices(services))
            .WithFeature(services => new BackgroundActivitiesFeature { RunWorkers = false }.ConfigureServices(services))
            .ConfigureServices(services =>
            {
                services.AddLogging().AddSingleton(new RuntimeReplaySafeFusionOptions { Enabled = false })
                    .AddScoped<IRuntimeResumptionService, Elsa.Workflows.Runtime.Services.Recovery.RuntimeResumptionService>();
                configure?.Invoke(services);
            })
            .Build(["actexec-parent", .. Ids]);

    private static readonly WorkflowAlterationAuthorityScope Authority =
        new(WorkflowExecutionHarness.Partition.Value, "activity-behavioural-drive", "activity-behavioural-drive");

    private static async Task<WorkflowAlterationJobState> SubmitAsync(WorkflowExecutionHarness harness, string kind, object payload, string idempotencyKey)
    {
        WorkflowAlterationPlanAdmissionResult admission;
        await using (var scope = harness.Services.CreateAsyncScope())
            admission = await scope.ServiceProvider.GetRequiredService<WorkflowAlterationPlanService>().SubmitAsync(
                new WorkflowAlterationSubmission(
                    Authority,
                    WorkflowAlterationTargetSelector.ForExecutionIds([harness.ExecutionId]),
                    [new WorkflowAlterationEnvelope(kind, 1, JsonSerializer.SerializeToElement(payload))]),
                new WorkflowAlterationOperatorProvenance("operator-1", correlationId: null),
                idempotencyKey);
        var jobId = WorkflowAlterationIdentity.CreateJobId(admission.Plan.PlanId, harness.ExecutionId);
        WorkflowAlterationJobState? job = null;
        for (var sweep = 0; sweep < 8 && job?.Status is not (WorkflowAlterationJobStatus.Succeeded or WorkflowAlterationJobStatus.Failed); sweep++)
        {
            await using var scope = harness.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<WorkflowAlterationOrchestrationSweep>()
                .ExecuteAsync(new WorkflowAlterationOrchestrationOptions { WorkerId = "test-worker" }, "test-worker");
            job = await scope.ServiceProvider.GetRequiredService<IWorkflowAlterationStore>().FindJobAsync(jobId);
        }

        return job!;
    }

    // Starts the run, then plays the worker: each admitted piece of work runs and its result is delivered, until none is left.
    private static async Task<WorkflowExecutionRun> RunAsync(WorkflowExecutionHarness harness, WorkflowExecutable executable, bool allowFault = false)
    {
        var reference = await harness.PublishAsync(executable, "background-refresh");
        await harness.StartPublishedAsync(reference, harness.ExecutionId, tenantId: WorkflowExecutionHarness.Partition.Value);
        var run = await DrainWorkersAsync(harness);
        if (!allowFault)
            run.AssertWorkflowCompleted();
        return run;
    }

    // Plays the runtime and the worker in the run's partition: sweep the scheduler, run each admitted piece of work,
    // deliver its receipt, until no work is left.
    private static async Task<WorkflowExecutionRun> DrainWorkersAsync(WorkflowExecutionHarness harness)
    {
        var worker = harness.Services.GetRequiredService<BackgroundActivityWorker>();
        for (var round = 0; round < 6; round++)
        {
            BackgroundActivityWorkClaim? claim;
            await using (var scope = await harness.Services.GetRequiredService<IPersistenceOperationScopeFactory>()
                             .CreateAsync(new PersistenceScope(WorkflowExecutionHarness.Partition.Value)))
            {
                var runtime = scope.ServiceProvider.GetRequiredService<IRuntimeResumptionService>();
                for (var pass = 0; pass < 4; pass++)
                    await runtime.SweepAsync(new RuntimeResumptionSweepRequest(backlogBatchSize: 100, recoveryScanBatchSize: 100, maxExecutionsPerSweep: 100));
                // The work store is partitioned: claims are taken inside the run's persistence scope, as a worker does.
                claim = await scope.ServiceProvider.GetRequiredService<IBackgroundActivityWorkStore>()
                    .ClaimNextAsync("refresh-test-worker", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
            }

            if (claim is null)
                break;
            await worker.ExecuteClaimAsync(new PersistenceScope(WorkflowExecutionHarness.Partition.Value), claim);
        }

        return await harness.ReadRunAsync(harness.ExecutionId);
    }
}

/// <summary>A background-safe leaf that records the <c>Count</c> it ran on, faults while its budget lasts, and otherwise returns <c>Count + 1</c>.</summary>
[BackgroundExecutionSafe]
public sealed class FlakyBackgroundIncrementActivity(FailureBudget budget, CountReadLog reads) : Activity<Incremented>
{
    [ActivityInput]
    public int Count { get; set; }

    protected override ValueTask<ActivityTransition<Incremented>> ExecuteAsync(ActivityExecutionContext context)
    {
        reads.Add(Count, context.OperationKey, context.LogicalInvocationId);
        return ValueTask.FromResult(budget.TakeFailure()
            ? ActivityTransition.Fault<Incremented>(new ActivityFault("TEST-FLAKY", "The service was unavailable."))
            : ActivityTransition.Complete(new Incremented(Count + 1), ActivityOutcomes.Done));
    }
}

/// <summary>A background-safe leaf that returns <c>Count + 1</c>.</summary>
[BackgroundExecutionSafe]
public sealed class BackgroundIncrementActivity : Activity<Incremented>
{
    [ActivityInput]
    public int Count { get; set; }

    protected override ValueTask<ActivityTransition<Incremented>> ExecuteAsync(ActivityExecutionContext context) =>
        ValueTask.FromResult(ActivityTransition.Complete(new Incremented(Count + 1), ActivityOutcomes.Done));
}
