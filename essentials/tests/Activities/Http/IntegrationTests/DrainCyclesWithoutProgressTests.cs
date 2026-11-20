using System.Net;
using Elsa.Activities.Http.Activities;
using Elsa.Activities.Http.Options;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Diagnostics;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit.Abstractions;
using Activity = System.Diagnostics.Activity;

namespace Elsa.Activities.Http.IntegrationTests;

/// <summary>
/// Drains far longer than the drain cycle limit complete under the default options in every persistence mode (#2661). The
/// limit counts only cycles that dispatch no work item new to the drain, so a run that keeps advancing is not cut off. Each
/// shape here spends cycles hop by hop: an External step precedes each WriteLine beside the suspended/faulted branch,
/// so the shape still crosses the limit when suspension retains folding. Activities without a ReplaySafe profile are
/// never fused.
/// Every test first checks that its drains ran more cycles than the limit, so a shape that stopped needing them would fail
/// here instead of passing without covering anything. <see cref="ShapeLength"/> keeps every shape past the limit in every
/// mode.
/// </summary>
public sealed class DrainCyclesWithoutProgressTests(ITestOutputHelper output)
{
    private const int ShapeLength = 30;
    private const string TriggerPath = "drain-cycles/parallel";

    private readonly DrainCycleCounter _cycles = new();
    private readonly ExecutableNode[] _lines = ReferenceWorkflows.WriteLines(ShapeLength);

    public static TheoryData<PersistenceMode> Modes() => new(Enum.GetValues<PersistenceMode>());

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task A_run_whose_Parallel_suspends_one_branch_runs_every_WriteLine_in_the_other(PersistenceMode mode)
    {
        await using var fixture = await StartHostAsync(mode);
        var executable = ReferenceWorkflows.Executable(
            "parallel-event-lines",
            ReferenceWorkflows.Parallel("fork", ("wait", ReferenceWorkflows.EventWait()), ("lines", WriteLines())),
            [],
            ReferenceWorkflows.EventResumeTarget());
        await fixture.PublishAsync(executable);

        var start = await fixture.Services.GetRequiredService<IWorkflowStartDispatcher>().DispatchAsync(
            new WorkflowExecutionStartDispatchRequest(executable.Identity.ArtifactId, requestedBy: "integration-test"));

        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted, start.CommandDispatch.Status);
        await AssertSuspendedOnTheEventAfterAllLinesAsync(fixture, start.WorkflowExecutionId);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task An_HTTP_triggered_run_answers_after_every_WriteLine_beside_a_suspended_branch(PersistenceMode mode)
    {
        await using var fixture = await StartHostAsync(mode);
        var endpoint = ReferenceWorkflows.SyncPostEndpoint("http-in", TriggerPath);
        await fixture.PublishAsync(ReferenceWorkflows.Executable(
            "http-parallel-event-lines",
            ReferenceWorkflows.Sequence(
                "root",
                endpoint,
                ReferenceWorkflows.Parallel("fork", ("wait", ReferenceWorkflows.EventWait()), ("lines", WriteLines(ReferenceWorkflows.DoneResponse())))),
            [],
            ReferenceWorkflows.ResumeTarget(endpoint.ExecutableNodeId, HttpEndpoint.ResumeTargetId),
            ReferenceWorkflows.EventResumeTarget()));

        using var response = await fixture.Client.PostAsync($"/workflows/http/{TriggerPath}", content: null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("done", await response.Content.ReadAsStringAsync());
        await AssertSuspendedOnTheEventAfterAllLinesAsync(fixture, (await fixture.SingleWorkflowExecutionAsync()).WorkflowExecutionId);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task A_sequence_of_activities_without_a_ReplaySafe_profile_completes(PersistenceMode mode)
    {
        await using var fixture = await StartHostAsync(mode);
        var steps = Enumerable.Range(1, ShapeLength)
            .Select(step => ReferenceWorkflows.Clr($"external-{step}", typeof(ExternalStepActivity), []))
            .ToArray();
        Assert.Equal(SideEffectProfile.External, steps[0].ActivityContract!.SideEffectProfile);
        var executable = ReferenceWorkflows.Executable("external-sequence", ReferenceWorkflows.Sequence("root", steps));
        await fixture.PublishAsync(executable);

        var executionId = await fixture.StartWorkflowDirectlyAsync(executable.Identity.ArtifactId);

        Assert.Equal(WorkflowExecutionStatus.Completed, (await fixture.WorkflowExecutionAsync(executionId)).Status);
        await AssertCompletedAsync(fixture, executionId, steps);
        await AssertDrainedPastTheLimitAsync(fixture);
    }

    [Theory]
    [MemberData(nameof(Modes))]
    public async Task A_run_whose_Parallel_faults_one_branch_runs_every_WriteLine_in_the_other_and_faults_through_its_incident_strategy(PersistenceMode mode)
    {
        await using var fixture = await StartHostAsync(mode);
        var executable = ReferenceWorkflows.Executable(
            "parallel-fault-lines",
            ReferenceWorkflows.Parallel("fork", ("fault", ReferenceWorkflows.Clr("fault", typeof(Fault), [])), ("lines", WriteLines())));
        await fixture.PublishAsync(executable);

        var executionId = await fixture.StartWorkflowDirectlyAsync(executable.Identity.ArtifactId);

        Assert.Equal(WorkflowExecutionStatus.Faulted, (await fixture.WorkflowExecutionAsync(executionId)).Status);
        await using (var scope = fixture.Services.CreateAsyncScope())
            Assert.Single(await scope.ServiceProvider.GetRequiredService<IIncidentStateStore>().ListAsync(executionId), incident => incident.ExecutableNodeId == "fault");
        await AssertCompletedAsync(fixture, executionId, _lines);
        await AssertDrainedPastTheLimitAsync(fixture);
    }

    private async Task<HttpEndpointHostFixture> StartHostAsync(PersistenceMode mode) =>
        await HttpEndpointHostFixture.StartDurableSqliteAsync(mode, ReferenceWorkflowRuns.MaxActivitiesPerCommit, services =>
        {
            ReferenceWorkflowRuns.ComposeHost(services);
            services.Replace(ServiceDescriptor.Singleton<IWorkflowEngineTracer>(_cycles));
            // These deliberately long drains test progress, not the HTTP deadline. Keep the client's
            // existing 100-second bound and opt into a longer server limit for these workflow shapes.
            services.Configure<HttpEndpointOptions>(options => options.SyncRequestTimeout = TimeSpan.FromSeconds(90));
        });

    private ExecutableNode WriteLines(params ExecutableNode[] after) => ReferenceWorkflows.Sequence("lines",
        [.. _lines.SelectMany((line, index) => new[]
        {
            ReferenceWorkflows.Clr($"unfused-{index}", typeof(ExternalStepActivity), []), line
        }), .. after]);

    /// <summary>
    /// The run is still waiting on its Event bookmark and nothing else: every WriteLine completed and the drain left no
    /// work behind. A drain that gave up part-way would leave the run in the same state with lines or continuations left.
    /// </summary>
    private async Task AssertSuspendedOnTheEventAfterAllLinesAsync(HttpEndpointHostFixture fixture, string executionId)
    {
        Assert.Equal(WorkflowExecutionStatus.Running, (await fixture.WorkflowExecutionAsync(executionId)).Status);
        Assert.Equal(ReferenceWorkflows.EventNodeId, Assert.Single(await fixture.ListBookmarksAsync(executionId)).ExecutableNodeId);
        await AssertCompletedAsync(fixture, executionId, _lines);
        await AssertDrainedPastTheLimitAsync(fixture);
    }

    private async Task AssertDrainedPastTheLimitAsync(HttpEndpointHostFixture fixture)
    {
        output.WriteLine($"Drain cycles: {_cycles.Count}");
        Assert.True(
            _cycles.Count > WorkflowDrainOrchestratorOptions.DefaultMaxCyclesWithoutProgress,
            $"The run took {_cycles.Count} drain cycles, no more than the limit of {WorkflowDrainOrchestratorOptions.DefaultMaxCyclesWithoutProgress}: this shape no longer covers a drain longer than the limit.");
        await ReferenceWorkflowRuns.AssertNothingLeftToDrainAsync(fixture);
    }

    private static async Task AssertCompletedAsync(HttpEndpointHostFixture fixture, string executionId, IEnumerable<ExecutableNode> nodes)
    {
        var completed = (await fixture.Services.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(executionId))
            .Where(state => state.Status == ActivityExecutionStatus.Completed)
            .Select(state => state.Execution.ExecutableNodeId)
            .ToHashSet(StringComparer.Ordinal);
        Assert.All(nodes, node => Assert.Contains(node.ExecutableNodeId, completed));
    }

    /// <summary>Counts the scheduler's drain cycles through the engine tracer, which every cycle starts a span on.</summary>
    private sealed class DrainCycleCounter : IWorkflowEngineTracer
    {
        private int _count;

        public int Count => Volatile.Read(ref _count);

        public Activity? StartDrainCycle(RuntimeSchedulerDrainRequest request)
        {
            Interlocked.Increment(ref _count);
            return null;
        }

        public Activity? StartDispatch(RuntimeSchedulerWorkItem workItem) => null;

        public Activity? StartActivityExecution(RuntimeSchedulerWorkItem workItem) => null;

        public Activity? StartCheckpointCommit(RuntimeCheckpointCommit commit) => null;
    }
}
