using System.Text.Json;
using Elsa.Activities.ControlFlow;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Switch.Models;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Switch.Tests;

public sealed class SwitchLifecycleTests
{
    [Fact]
    public async Task Independent_waits_resume_in_reverse_order_before_one_Switch_completion()
    {
        await using var harness = Harness();
        var executable = Executable(Wait("a"), Wait("c"));
        var run = await harness.RunAsync(executable);
        Assert.NotEqual(ActivityExecutionStatus.Completed, run.State("switch").Status);
        var bookmarks = await Bookmarks(harness);
        Assert.Equal(2, bookmarks.Count);
        var c = Assert.Single(bookmarks, item => item.ExecutableNodeId == "c");
        var partial = await Resume(harness, executable.Identity, c);
        partial.AssertCompleted("c");
        Assert.NotEqual(ActivityExecutionStatus.Completed, partial.State("switch").Status);
        var a = Assert.Single(await Bookmarks(harness));
        var completed = await Resume(harness, executable.Identity, a);
        completed.AssertOutcomes("switch", "Approved", "Notify");
        completed.AssertWorkflowCompleted();
        Assert.Single(completed.States("a"));
        Assert.Single(completed.States("c"));
        Assert.Empty(await Bookmarks(harness));
    }

    [Fact]
    public async Task One_completed_body_does_not_complete_a_waiting_selected_sibling()
    {
        await using var harness = Harness();
        var executable = Executable(WorkflowExecutionHarness.NewProbeNode("a"), Wait("c"));
        var run = await harness.RunAsync(executable);
        run.AssertCompleted("a");
        Assert.NotEqual(ActivityExecutionStatus.Completed, run.State("switch").Status);
        var completed = await Resume(harness, executable.Identity, Assert.Single(await Bookmarks(harness)));
        completed.AssertOutcomes("switch", "Approved", "Notify");
        completed.AssertWorkflowCompleted();
        Assert.Single(completed.States("a"));
    }

    [Fact]
    public async Task Break_cancels_a_selected_live_wait_and_removes_its_bookmark()
    {
        await using var harness = Harness();
        var run = await harness.RunAsync(Executable(Wait("a"), WorkflowExecutionHarness.NewProbeNode("c", [ActivityOutcomes.Break])));
        run.AssertOutcomes("switch", ActivityOutcomes.Break);
        Assert.Equal(ActivityExecutionStatus.Cancelled, run.State("a").Status);
        Assert.Empty(await Bookmarks(harness));
        run.AssertWorkflowCompleted();
    }

    [Fact]
    public async Task A_faulted_body_preserves_the_blocking_policy_and_cannot_count_as_success()
    {
        await using var harness = Harness();
        var run = await harness.RunAsync(Executable(WorkflowExecutionHarness.NewProbeNode("a"), WorkflowExecutionHarness.NewFaultingNode("c")));
        run.AssertCompleted("a");
        Assert.Equal(ActivityExecutionStatus.Faulted, run.State("c").Status);
        Assert.NotEmpty(run.State("c").IncidentIds);
        Assert.NotEqual(ActivityExecutionStatus.Completed, run.State("switch").Status);
        Assert.Null(run.State("switch").Completion);
    }

    private static WorkflowExecutionHarness Harness() => WorkflowExecutionHarness.Create()
        .WithFeature(services => new ActivitiesControlFlowFeature().ConfigureServices(services))
        .WithProbeLeaf().WithFaultingLeaf().Build(Enumerable.Range(1, 30).Select(value => $"switch-lifecycle-{value}"));

    [Fact]
    public async Task Break_suppresses_a_selected_sibling_that_has_not_started()
    {
        await using var harness = Harness();
        var run = await harness.RunAsync(Executable(WorkflowExecutionHarness.NewProbeNode("a", [ActivityOutcomes.Break]),
            WorkflowExecutionHarness.NewProbeNode("c")));
        run.AssertOutcomes("switch", ActivityOutcomes.Break);
        Assert.All(run.States("c"), state =>
        {
            Assert.Null(state.StartedAt);
            Assert.NotEqual(ActivityExecutionStatus.Completed, state.Status);
        });
        run.AssertWorkflowCompleted();
    }

    [Fact]
    public async Task Workflow_Finish_cancels_a_waiting_case_without_successfully_completing_Switch()
    {
        await using var harness = Harness();
        var finish = new ExecutableNode("c", "c", "elsa.intrinsic.finish", "1.0.0", "intrinsic",
            JsonSerializer.SerializeToElement(new { kind = "Finish", schemaVersion = "1.0.0" }),
            new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>(), intrinsicKind: WorkflowIntrinsicKind.Finish);
        var run = await harness.RunAsync(Executable(Wait("a"), finish), allowPendingWorkOnTerminalCompletion: true);
        run.AssertWorkflowCompleted();
        Assert.NotEqual(ActivityExecutionStatus.Completed, run.State("switch").Status);
        Assert.Equal(ActivityExecutionStatus.Cancelled, run.State("a").Status);
        Assert.Empty(await Bookmarks(harness));
    }

    [Fact]
    public async Task Workflow_cancellation_cancels_both_independent_waits_and_their_bookmarks()
    {
        await using var harness = Harness();
        var reference = await harness.PublishAsync(Executable(Wait("a"), Wait("c")), "switch-cancel-source");
        const string runId = "switch-cancel-run";
        await harness.StartPublishedAsync(reference, runId);
        var bookmarks = harness.Services.GetRequiredService<IBookmarkStateStore>();
        Assert.Equal(2, (await bookmarks.ListAllBookmarkStatesAsync(runId)).Count);
        await harness.CancelAsync(runId);
        Assert.Empty(await bookmarks.ListAllBookmarkStatesAsync(runId));
        var states = await harness.Services.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(runId);
        Assert.Equal(3, states.Count);
        Assert.All(states, state => Assert.Equal(ActivityExecutionStatus.Cancelled, state.Status));
        Assert.All(states, state => Assert.Null(state.Completion));
    }

    internal static ExecutableNode Wait(string id) => new(id, id, typeof(SwitchWaitActivity).FullName!, "1.0.0",
        "test/wait", JsonSerializer.SerializeToElement(new { }), new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>());

    internal static WorkflowExecutable Executable(ExecutableNode first, ExecutableNode last)
    {
        var root = SwitchMatchAnyTests.Executable(SwitchModes.MatchAny, true, false, true,
            new Dictionary<string, ExecutableNode> { ["a"] = first, ["c"] = last }).RootActivity;
        var waiting = new[] { first, last }.Where(node => node.ActivityType == typeof(SwitchWaitActivity).FullName!);
        return new(WorkflowExecutionHarness.Identity, root,
            waiting.ToDictionary(node => $"resume:{node.ExecutableNodeId}:wait", node => new WorkflowExecutableResumeTarget(
                $"resume:{node.ExecutableNodeId}:wait", node.ExecutableNodeId, "ResumeAsync", new Dictionary<string, string>(), LocalResumeTargetId: "wait")),
            DateTimeOffset.UnixEpoch, new Dictionary<string, string>(), IncidentStrategyBuiltIns.FaultReference);
    }

    private static async Task<IReadOnlyCollection<BookmarkState>> Bookmarks(WorkflowExecutionHarness harness) =>
        await harness.Services.GetRequiredService<IBookmarkStateStore>().ListAllBookmarkStatesAsync(WorkflowExecutionHarness.WorkflowExecutionId);

    private static Task<WorkflowExecutionRun> Resume(WorkflowExecutionHarness harness, WorkflowExecutableIdentity identity, BookmarkState bookmark) =>
        harness.ResumeAsync(identity, bookmark.BookmarkId, bookmark.ActivityExecutionId, bookmark.ExecutableNodeId,
            bookmark.ResumeTargetId, bookmark.StimulusType, bookmark.StimulusHash, JsonSerializer.SerializeToElement(new SwitchWaitTrigger(true)));
}

public sealed record SwitchWaitState(string Marker);
public sealed record SwitchWaitTrigger(bool Released);

public sealed class SwitchWaitActivity : StatefulActivity<ActivityUnit, SwitchWaitState, SwitchWaitTrigger>
{
    protected override ValueTask<ActivityTransition<ActivityUnit, SwitchWaitState>> ExecuteAsync(ActivityExecutionContext context) =>
        ValueTask.FromResult(Suspend(new SwitchWaitState("waiting"), [new ActivityTriggerRegistration<SwitchWaitTrigger>("wait", "SwitchWait", "switch-wait")]));

    protected override ValueTask<ActivityTransition<ActivityUnit, SwitchWaitState>> ResumeAsync(ActivityResumeContext<SwitchWaitState, SwitchWaitTrigger> context) =>
        ValueTask.FromResult(Complete(ActivityUnit.Value, ActivityOutcomes.Done));
}
