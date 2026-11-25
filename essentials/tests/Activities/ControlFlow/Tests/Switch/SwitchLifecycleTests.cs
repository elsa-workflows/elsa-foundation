using System.Text.Json;
using Elsa.Activities.ControlFlow;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Switch.Models;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.WorkHandlers;
using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;
using Elsa.Workflows.Runtime.Services.Alterations;
using Elsa.Workflows.Runtime.Services.Alterations.Handlers;
using Elsa.Workflows.Runtime.Services.Bookmarks;
using Elsa.Workflows.Runtime.Services.Incidents;
using Elsa.Activities.Sequence;
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
        Assert.Contains(RuntimeMetadataKeys.ParentCompletionProcessedWorkItemId, run.State("c").Metadata.Keys);
        // Isolate parent admission in a projected nonterminal workflow after workflow/incident recovery. The
        // actual terminal fault and live incident correctly refuse earlier; neither grants a parent capability.
        var staging = new AlterationStaging();
        var planner = new ActivityExecutionSupersessionPlanner(new ActivityScopeCleanupStore(
            harness.Services.GetRequiredService<IBookmarkStateStore>(), harness.Services.GetRequiredService<IDurableTimerStore>(),
            harness.Services.GetRequiredService<IWorkflowSchedulerWorkQueue>()));
        var handler = new RescheduleActivityAlterationHandler(harness.Services.GetRequiredService<IWorkflowExecutableStore>(),
            new InMemoryIncidentStateStore(), planner, TimeProvider.System);
        var job = new WorkflowAlterationJobState("job", "plan", harness.ExecutionId, "tenant", 0, WorkflowAlterationJobStatus.Running,
            new WorkflowAlterationJobClaim("worker", "claim", DateTimeOffset.MaxValue), 1, [], null, null, DateTimeOffset.UnixEpoch, null, null, 0);
        var result = await handler.PreflightAsync(new WorkflowAlterationPreflightContext(job,
            new WorkflowAlterationEnvelope("RescheduleActivity", 1, JsonSerializer.SerializeToElement(new { sourceActivityExecutionId = run.State("c").InvocationId })),
            0, new WorkflowAlterationProjectedState([run.WorkflowState! with { Status = WorkflowExecutionStatus.Running, CompletedAt = null },
                new WorkflowAlterationActorCommandExecutor.WorkflowAlterationActivityExecutionProjection(run.ActivityStates)]), staging));
        Assert.False(result.IsAccepted);
        Assert.Equal("ParentDoesNotAcceptRescheduledChild", result.Failure?.Code);
        Assert.Empty(staging.StagedChanges);
    }

    private sealed class AlterationStaging : IWorkflowAlterationStagingWorkspace
    {
        public IReadOnlyList<IWorkflowAlterationStagedChange> StagedChanges { get; } = [];
        public void Stage(IWorkflowAlterationStagedChange change) => throw new InvalidOperationException("A refused Switch alteration must not stage a change.");
    }

    private static WorkflowExecutionHarness Harness() => WorkflowExecutionHarness.Create()
        .WithFeature(services => { new ActivitiesControlFlowFeature().ConfigureServices(services); new ActivitiesSequenceFeature().ConfigureServices(services); })
        .WithProbeLeaf().WithFaultingLeaf().Build(Enumerable.Range(1, 30).Select(value => $"switch-lifecycle-{value}"));

    [Fact]
    public async Task A_resumed_Break_cancels_a_selected_body_subtree_with_an_existing_nested_bookmark()
    {
        await using var harness = Harness();
        var nested = Wait("nested-wait");
        var after = WorkflowExecutionHarness.NewProbeNode("must-not-start");
        var sequence = new ExecutableNode("a", "a", typeof(Elsa.Activities.Sequence.Activities.Sequence).FullName!, "1.0.0", "test/sequence", JsonSerializer.SerializeToElement(new { }),
            new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>(), childSlots: [new("Sequence.Activities", [nested, after])],
            structure: new("elsa.sequence.structure", "1.0.0", JsonSerializer.SerializeToElement(new { activities = new[] { "nested-wait", "must-not-start" } })));
        var executable = Executable(sequence, Wait("c"));
        var waiting = await harness.RunAsync(executable);
        Assert.Equal(ActivityExecutionStatus.Suspended, waiting.State("nested-wait").Status);
        var bookmarks = await Bookmarks(harness);
        Assert.Equal(2, bookmarks.Count);
        var run = await Resume(harness, executable.Identity, Assert.Single(bookmarks, item => item.ExecutableNodeId == "c"), @break: true);
        run.AssertOutcomes("c", ActivityOutcomes.Break);
        run.AssertOutcomes("switch", ActivityOutcomes.Break);
        Assert.Equal(ActivityExecutionStatus.Cancelled, run.State("a").Status);
        Assert.Equal(ActivityExecutionStatus.Cancelled, run.State("nested-wait").Status);
        run.AssertDidNotRun("must-not-start");
        Assert.Empty(await Bookmarks(harness));
        run.AssertWorkflowCompleted();
    }

    [Fact]
    public async Task Break_suppresses_a_selected_sibling_that_has_not_started()
    {
        var hold = new HoldSelectedStart();
        await using var harness = WorkflowExecutionHarness.Create()
            .WithFeature(services => { new ActivitiesControlFlowFeature().ConfigureServices(services); new ActivitiesSequenceFeature().ConfigureServices(services); })
            .ConfigureServices(services =>
            {
                // Hold a real Start item on the supported discrete-hop path; fusion otherwise elides it.
                services.AddSingleton(new RuntimeReplaySafeFusionOptions { Enabled = false });
                hold.Decorate(services);
            }).WithProbeLeaf().Build(Enumerable.Range(1, 30).Select(value => $"held-switch-{value}"));
        var owner = Executable(WorkflowExecutionHarness.NewProbeNode("a", [ActivityOutcomes.Break]), WorkflowExecutionHarness.NewProbeNode("c")).RootActivity;
        var condition = ValueEnvelope.Inline(new("Boolean"), JsonSerializer.SerializeToElement(true), ValueProtectionPolicy.InstanceInline);
        var loop = new ExecutableNode("loop", "loop", typeof(Elsa.Activities.While.Activities.While).FullName!, "1.0.0", "test/loop", JsonSerializer.SerializeToElement(new { }),
            new Dictionary<string, RuntimeInputBinding> { ["Condition"] = new("Condition", new("Boolean"), ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Literal, literal: condition) },
            new Dictionary<string, string>(), childSlots: [new("While.Body", [owner])],
            structure: new("elsa.while.structure", "1.0.0", JsonSerializer.SerializeToElement(new { body = "switch" })));
        var after = Wait("after");
        var root = new ExecutableNode("root", "root", typeof(Elsa.Activities.Sequence.Activities.Sequence).FullName!, "1.0.0", "test/root", JsonSerializer.SerializeToElement(new { }),
            new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>(), childSlots: [new("Sequence.Activities", [loop, after])],
            structure: new("elsa.sequence.structure", "1.0.0", JsonSerializer.SerializeToElement(new { activities = new[] { "loop", "after" } })));
        var executable = new WorkflowExecutable(WorkflowExecutionHarness.Identity, root,
            new Dictionary<string, WorkflowExecutableResumeTarget> { ["resume:after:wait"] = new("resume:after:wait", "after", "ResumeAsync", new Dictionary<string, string>(), LocalResumeTargetId: "wait") },
            DateTimeOffset.UnixEpoch, new Dictionary<string, string>(), IncidentStrategyBuiltIns.FaultReference);
        var run = await harness.RunAsync(executable);
        run.AssertOutcomes("switch", ActivityOutcomes.Break);
        run.AssertCompleted("loop");
        Assert.NotNull(hold.Captured);
        Assert.Equal(ActivityExecutionStatus.Suspended, run.State("after").Status);
        Assert.Equal(ActivityExecutionStatus.Cancelled, run.State("c").Status);
        hold.Released = true;
        await harness.Services.GetRequiredService<IWorkflowSchedulerWorkQueue>().EnqueueAsync(hold.Captured!);
        var completed = await Resume(harness, executable.Identity, Assert.Single(await Bookmarks(harness)));
        Assert.All(completed.States("c"), state =>
        {
            Assert.Null(state.StartedAt);
            Assert.NotEqual(ActivityExecutionStatus.Completed, state.Status);
        });
        completed.AssertWorkflowCompleted();
    }

    private sealed class HoldSelectedStart : IWorkflowSchedulerWorkHandler, IRuntimePipelineWorkHandler
    {
        private IWorkflowSchedulerWorkHandler _inner = null!;
        public RuntimeSchedulerWorkItem? Captured { get; private set; }
        public bool Released { get; set; }
        public string Name => _inner.Name;
        public bool CanHandle(RuntimeSchedulerWorkItem item) => _inner.CanHandle(item);
        public ValueTask HandleAsync(RuntimeSchedulerWorkItem item, CancellationToken cancellationToken = default)
        {
            if (!Released && item.Payload is { } payload
                && payload.TryGetProperty(JsonNamingPolicy.CamelCase.ConvertName(nameof(RuntimeStartActivityCommandPayload.ExecutableNodeId)), out var id) && id.GetString() == "c")
            {
                Assert.Null(Captured);
                Captured = item;
                return ValueTask.CompletedTask;
            }
            return _inner.HandleAsync(item, cancellationToken);
        }
        public ValueTask HandleAsync(RuntimeSchedulerWorkItem item, IRuntimePipelineContext context, CancellationToken cancellationToken = default)
        {
            if (!Released && item.Payload is { } payload
                && payload.TryGetProperty(JsonNamingPolicy.CamelCase.ConvertName(nameof(RuntimeStartActivityCommandPayload.ExecutableNodeId)), out var id) && id.GetString() == "c")
                return HandleAsync(item, cancellationToken);
            return ((IRuntimePipelineWorkHandler)_inner).HandleAsync(item, context, cancellationToken);
        }
        public void Decorate(IServiceCollection services)
        {
            var descriptor = services.Single(item => item.ServiceType == typeof(IWorkflowSchedulerWorkHandler)
                && item.ImplementationType == typeof(WorkflowStartActivitySchedulerWorkHandler));
            services.Remove(descriptor);
            services.Add(ServiceDescriptor.Describe(typeof(IWorkflowSchedulerWorkHandler), provider =>
            {
                _inner = ActivatorUtilities.CreateInstance<WorkflowStartActivitySchedulerWorkHandler>(provider);
                return this;
            }, descriptor.Lifetime));
        }
    }

    [Fact]
    public async Task Workflow_Finish_retains_its_terminal_semantics_without_successfully_completing_Switch()
    {
        await using var harness = Harness();
        var finish = new ExecutableNode("c", "c", "elsa.intrinsic.finish", "1.0.0", "intrinsic",
            JsonSerializer.SerializeToElement(new { kind = "Finish", schemaVersion = "1.0.0" }),
            new Dictionary<string, RuntimeInputBinding>
            {
                [WorkflowIntrinsicInputKeys.Outcome] = new(WorkflowIntrinsicInputKeys.Outcome, new("String"),
                    ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Literal,
                    literal: ValueEnvelope.Inline(new("String"), JsonSerializer.SerializeToElement(ActivityOutcomes.Done), ValueProtectionPolicy.InstanceInline))
            }, new Dictionary<string, string>(), intrinsicKind: WorkflowIntrinsicKind.Finish);
        var run = await harness.RunAsync(Executable(Wait("a"), finish), allowPendingWorkOnTerminalCompletion: true);
        run.AssertWorkflowCompleted();
        Assert.NotEqual(ActivityExecutionStatus.Completed, run.State("switch").Status);
        // Existing Finish (#293) terminates the run and abandons further dispatch; it does not turn already
        // suspended child history into successful Switch completion or an enclosing cancellation operation.
        Assert.Equal(ActivityExecutionStatus.Suspended, run.State("a").Status);
        Assert.Single(await Bookmarks(harness));
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
        var waiting = new[] { first, last }.SelectMany(node => node.DescendantsAndSelf()).Where(node => node.ActivityType == typeof(SwitchWaitActivity).FullName!);
        return new(WorkflowExecutionHarness.Identity, root,
            waiting.ToDictionary(node => $"resume:{node.ExecutableNodeId}:wait", node => new WorkflowExecutableResumeTarget(
                $"resume:{node.ExecutableNodeId}:wait", node.ExecutableNodeId, "ResumeAsync", new Dictionary<string, string>(), LocalResumeTargetId: "wait")),
            DateTimeOffset.UnixEpoch, new Dictionary<string, string>(), IncidentStrategyBuiltIns.FaultReference);
    }

    private static async Task<IReadOnlyCollection<BookmarkState>> Bookmarks(WorkflowExecutionHarness harness) =>
        await harness.Services.GetRequiredService<IBookmarkStateStore>().ListAllBookmarkStatesAsync(WorkflowExecutionHarness.WorkflowExecutionId);

    private static Task<WorkflowExecutionRun> Resume(WorkflowExecutionHarness harness, WorkflowExecutableIdentity identity, BookmarkState bookmark, bool @break = false) =>
        harness.ResumeAsync(identity, bookmark.BookmarkId, bookmark.ActivityExecutionId, bookmark.ExecutableNodeId,
            bookmark.ResumeTargetId, bookmark.StimulusType, bookmark.StimulusHash, JsonSerializer.SerializeToElement(new SwitchWaitTrigger(true, @break), Elsa.Serialization.Core.JsonBaseline.Options));
}

public sealed record SwitchWaitState(string Marker);
public sealed record SwitchWaitTrigger(bool Released, bool Break = false);

[ActivityOutcome(ActivityOutcomes.Done)]
[ActivityOutcome(ActivityOutcomes.Break)]
public sealed class SwitchWaitActivity : StatefulActivity<ActivityUnit, SwitchWaitState, SwitchWaitTrigger>
{
    protected override ValueTask<ActivityTransition<ActivityUnit, SwitchWaitState>> ExecuteAsync(ActivityExecutionContext context) =>
        ValueTask.FromResult(Suspend(new SwitchWaitState("waiting"), [new ActivityTriggerRegistration<SwitchWaitTrigger>("wait", "SwitchWait", "switch-wait")]));

    protected override ValueTask<ActivityTransition<ActivityUnit, SwitchWaitState>> ResumeAsync(ActivityResumeContext<SwitchWaitState, SwitchWaitTrigger> context) =>
        ValueTask.FromResult(Complete(ActivityUnit.Value, context.Trigger.Break ? ActivityOutcomes.Break : ActivityOutcomes.Done));
}
