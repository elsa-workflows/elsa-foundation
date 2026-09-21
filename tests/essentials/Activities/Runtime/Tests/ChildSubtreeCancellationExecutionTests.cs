using System.Text.Json;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// Spec 112: a structural parent stages child-subtree cancellations during a child-completion
/// evaluation; the runtime terminalizes the subtree, cleans up its bookmarks and queued work, and
/// suppresses its incidents in the same checkpoint commit as the parent's continuation.
/// </summary>
public sealed class ChildSubtreeCancellationExecutionTests
{
    private const string ParentExecutionId = "actexec-parent";
    private const string TargetExecutionId = "actexec-b";

    [Fact]
    public async Task CompletingContinuation_CancelsWaitingSiblingAndCleansBookmark()
    {
        await using var harness = NewHarness(
            new SubtreeCancellationDirective(TargetExecutionId),
            "actexec-parent", "actexec-b", "actexec-a", "actexec-a-child");

        var run = await harness.RunAsync(NewExecutable(
            NewStructuralNode("node-parent", typeof(SubtreeCancellingParentActivity),
                NewWaitingNode("node-b"),
                NewStructuralNode("node-a", typeof(PassthroughStructuralActivity), WorkflowExecutionHarness.NewProbeNode("node-a-child"))),
            waitingNodeIds: ["node-b"]));

        run.AssertWorkflowCompleted();
        run.AssertCompleted("node-parent");
        var cancelled = run.State("node-b");
        Assert.Equal(ActivityExecutionStatus.Cancelled, cancelled.Status);
        Assert.Equal("ParentCancelled", cancelled.SubStatus);
        Assert.Equal("test-cancel", cancelled.Metadata[RuntimeMetadataKeys.ScopeCancellationReason]);
        Assert.Equal(ParentExecutionId, cancelled.Metadata[RuntimeMetadataKeys.SubtreeCancellationRequestedBy]);
        Assert.Empty(await ListBookmarksAsync(harness));
    }

    [Fact]
    public async Task DeferredContinuation_CancelsWaitingSiblingThenSchedulesNextChild()
    {
        await using var harness = NewHarness(
            new SubtreeCancellationDirective(TargetExecutionId) { DeferAndScheduleLateChild = true },
            "actexec-parent", "actexec-b", "actexec-a", "actexec-a-child", "actexec-late");

        var run = await harness.RunAsync(NewExecutable(
            NewStructuralNode("node-parent", typeof(SubtreeCancellingParentActivity),
                NewWaitingNode("node-b"),
                NewStructuralNode("node-a", typeof(PassthroughStructuralActivity), WorkflowExecutionHarness.NewProbeNode("node-a-child")),
                WorkflowExecutionHarness.NewProbeNode(SubtreeCancellingParentActivity.LateChildNodeId)),
            waitingNodeIds: ["node-b"]));

        run.AssertWorkflowCompleted();
        run.AssertCompleted("node-parent");
        run.AssertCompleted(SubtreeCancellingParentActivity.LateChildNodeId);
        Assert.Equal(ActivityExecutionStatus.Cancelled, run.State("node-b").Status);
        Assert.Empty(await ListBookmarksAsync(harness));
    }

    [Fact]
    public async Task NestedSubtree_CancelsGrandchildAndCleansItsBookmark()
    {
        await using var harness = NewHarness(
            new SubtreeCancellationDirective(TargetExecutionId),
            "actexec-parent", "actexec-b", "actexec-a", "actexec-b-wait", "actexec-a-child");

        var run = await harness.RunAsync(NewExecutable(
            NewStructuralNode("node-parent", typeof(SubtreeCancellingParentActivity),
                NewStructuralNode("node-b", typeof(PassthroughStructuralActivity), NewWaitingNode("node-b-wait")),
                NewStructuralNode("node-a", typeof(PassthroughStructuralActivity), WorkflowExecutionHarness.NewProbeNode("node-a-child"))),
            waitingNodeIds: ["node-b-wait"]));

        run.AssertWorkflowCompleted();
        Assert.Equal(ActivityExecutionStatus.Cancelled, run.State("node-b").Status);
        var grandchild = run.State("node-b-wait");
        Assert.Equal(ActivityExecutionStatus.Cancelled, grandchild.Status);
        Assert.Equal("ParentCancelled", grandchild.SubStatus);
        Assert.Empty(await ListBookmarksAsync(harness));
    }

    [Fact]
    public async Task TerminalTarget_IsSkippedAsFirstCompletionWinsRace()
    {
        // node-b is a plain probe: it completes first and its completion is the callback that stages
        // the cancellation targeting it. A terminal target is a legal race outcome, not an error.
        await using var harness = NewHarness(
            new SubtreeCancellationDirective(TargetExecutionId),
            "actexec-parent", "actexec-b", "actexec-a", "actexec-a-child");

        var run = await harness.RunAsync(
            NewExecutable(NewStructuralNode("node-parent", typeof(SubtreeCancellingParentActivity),
                WorkflowExecutionHarness.NewProbeNode("node-b"),
                NewStructuralNode("node-a", typeof(PassthroughStructuralActivity), WorkflowExecutionHarness.NewProbeNode("node-a-child")))),
            allowPendingWorkOnTerminalCompletion: true);

        run.AssertWorkflowCompleted();
        run.AssertCompleted("node-parent");
        Assert.Equal(ActivityExecutionStatus.Completed, run.State("node-b").Status);
    }

    [Fact]
    public async Task BlockingIncidentInsideCancelledSubtree_IsSuppressedAndWorkflowCompletes()
    {
        await using var harness = NewHarness(
            new SubtreeCancellationDirective(TargetExecutionId),
            "actexec-parent", "actexec-b", "actexec-a", "actexec-b-fault", "actexec-a-child");

        var run = await harness.RunAsync(NewExecutable(
            NewStructuralNode("node-parent", typeof(SubtreeCancellingParentActivity),
                NewStructuralNode("node-b", typeof(PassthroughStructuralActivity), WorkflowExecutionHarness.NewFaultingNode("node-b-fault")),
                NewStructuralNode("node-a", typeof(PassthroughStructuralActivity), WorkflowExecutionHarness.NewProbeNode("node-a-child")))));

        run.AssertWorkflowCompleted();
        Assert.Equal(ActivityExecutionStatus.Cancelled, run.State("node-b").Status);
        Assert.Equal(ActivityExecutionStatus.Faulted, run.State("node-b-fault").Status);
        var incident = Assert.Single(await harness.Services.GetRequiredService<IIncidentStateStore>()
            .ListAsync(WorkflowExecutionHarness.WorkflowExecutionId));
        Assert.Equal(IncidentStatus.Suppressed, incident.Status);
        Assert.NotNull(incident.ResolvedAt);
        Assert.Equal("test-cancel", incident.Metadata[RuntimeMetadataKeys.ScopeCancellationReason]);
    }

    [Fact]
    public async Task UnknownTarget_FaultsTheEvaluation()
    {
        await using var harness = NewHarness(
            new SubtreeCancellationDirective("actexec-missing"),
            "actexec-parent", "actexec-b", "actexec-a", "actexec-a-child");

        var run = await harness.RunAsync(
            NewExecutable(NewStructuralNode("node-parent", typeof(SubtreeCancellingParentActivity),
                NewWaitingNode("node-b"),
                NewStructuralNode("node-a", typeof(PassthroughStructuralActivity), WorkflowExecutionHarness.NewProbeNode("node-a-child"))),
                waitingNodeIds: ["node-b"]),
            allowPendingWorkOnTerminalCompletion: true);

        var parent = run.State("node-parent");
        Assert.Equal(ActivityExecutionStatus.Faulted, parent.Status);
        Assert.Contains("missing activity execution", parent.Fault!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NonChildTarget_FaultsTheEvaluation()
    {
        await using var harness = NewHarness(
            new SubtreeCancellationDirective(ParentExecutionId),
            "actexec-parent", "actexec-b", "actexec-a", "actexec-a-child");

        var run = await harness.RunAsync(
            NewExecutable(NewStructuralNode("node-parent", typeof(SubtreeCancellingParentActivity),
                NewWaitingNode("node-b"),
                NewStructuralNode("node-a", typeof(PassthroughStructuralActivity), WorkflowExecutionHarness.NewProbeNode("node-a-child"))),
                waitingNodeIds: ["node-b"]),
            allowPendingWorkOnTerminalCompletion: true);

        var parent = run.State("node-parent");
        Assert.Equal(ActivityExecutionStatus.Faulted, parent.Status);
        Assert.Contains("not a child of", parent.Fault!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DuplicateTarget_FaultsTheEvaluation()
    {
        await using var harness = NewHarness(
            new SubtreeCancellationDirective(TargetExecutionId) { StageTwice = true },
            "actexec-parent", "actexec-b", "actexec-a", "actexec-a-child");

        var run = await harness.RunAsync(
            NewExecutable(NewStructuralNode("node-parent", typeof(SubtreeCancellingParentActivity),
                NewWaitingNode("node-b"),
                NewStructuralNode("node-a", typeof(PassthroughStructuralActivity), WorkflowExecutionHarness.NewProbeNode("node-a-child"))),
                waitingNodeIds: ["node-b"]),
            allowPendingWorkOnTerminalCompletion: true);

        var parent = run.State("node-parent");
        Assert.Equal(ActivityExecutionStatus.Faulted, parent.Status);
        Assert.Contains("more than once", parent.Fault!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FaultContinuationWithCancellation_FaultsTheEvaluation()
    {
        await using var harness = NewHarness(
            new SubtreeCancellationDirective(TargetExecutionId) { FaultInsteadOfComplete = true },
            "actexec-parent", "actexec-b", "actexec-a", "actexec-a-child");

        var run = await harness.RunAsync(
            NewExecutable(NewStructuralNode("node-parent", typeof(SubtreeCancellingParentActivity),
                NewWaitingNode("node-b"),
                NewStructuralNode("node-a", typeof(PassthroughStructuralActivity), WorkflowExecutionHarness.NewProbeNode("node-a-child"))),
                waitingNodeIds: ["node-b"]),
            allowPendingWorkOnTerminalCompletion: true);

        var parent = run.State("node-parent");
        Assert.Equal(ActivityExecutionStatus.Faulted, parent.Status);
        Assert.Contains("cannot also cancel child subtrees", parent.Fault!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task InitialStructuralExecution_CannotStageCancellations()
    {
        await using var harness = NewHarness(
            new SubtreeCancellationDirective(TargetExecutionId),
            "actexec-parent", "actexec-child");

        var run = await harness.RunAsync(
            NewExecutable(NewStructuralNode("node-parent", typeof(InitialCancellationStructuralActivity),
                WorkflowExecutionHarness.NewProbeNode("node-child"))),
            allowPendingWorkOnTerminalCompletion: true);

        var parent = run.State("node-parent");
        Assert.Equal(ActivityExecutionStatus.Faulted, parent.Status);
        Assert.Contains("cannot cancel child subtrees", parent.Fault!.Message, StringComparison.Ordinal);
    }

    private static WorkflowExecutionHarness NewHarness(SubtreeCancellationDirective directive, params string[] activityExecutionIds) =>
        WorkflowExecutionHarness.Create()
            .WithProbeLeaf()
            .ConfigureServices(services => services.AddSingleton(directive))
            .Build(activityExecutionIds);

    private static async Task<IReadOnlyCollection<BookmarkState>> ListBookmarksAsync(WorkflowExecutionHarness harness) =>
        await harness.Services.GetRequiredService<IBookmarkStateStore>()
            .ListAllBookmarkStatesAsync(WorkflowExecutionHarness.WorkflowExecutionId);

    private static WorkflowExecutable NewExecutable(ExecutableNode root, IReadOnlyCollection<string>? waitingNodeIds = null) =>
        StructuralExecutionTestSupport.NewExecutable(root, waitingNodeIds);

    private static ExecutableNode NewStructuralNode(string nodeId, Type activityType, params ExecutableNode[] children) =>
        StructuralExecutionTestSupport.NewStructuralNode(nodeId, activityType, children);

    private static ExecutableNode NewWaitingNode(string nodeId) =>
        StructuralExecutionTestSupport.NewWaitingNode(nodeId);

    /// <summary>What the parent under test should do on its first child-completion callback.</summary>
    public sealed record SubtreeCancellationDirective(string TargetActivityExecutionId, string Reason = "test-cancel")
    {
        public bool FaultInsteadOfComplete { get; init; }
        public bool DeferAndScheduleLateChild { get; init; }
        public bool StageTwice { get; init; }
    }

    /// <summary>
    /// Schedules every slot child except the designated late child; on the first child completion it
    /// stages the directive's subtree cancellation and completes, faults, or defers per the directive.
    /// </summary>
    public sealed class SubtreeCancellingParentActivity(SubtreeCancellationDirective directive) : StructuralActivity,
        IRuntimeStructuralActivity,
        IRuntimeActivityChildCompletionHandler
    {
        public const string LateChildNodeId = "node-late";

        public ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext context)
        {
            foreach (var child in Assert.Single(context.ExecutableNode.ChildSlots).Activities)
                if (!StringComparer.Ordinal.Equals(child.ExecutableNodeId, LateChildNodeId))
                    context.ScheduleChildActivity(child.ExecutableNodeId, context.ActivityExecutionState.InvocationId);
            return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
        }

        public ValueTask<RuntimeStructuralContinuation> OnChildCompletedAsync(ActivityChildCompletedContext context)
        {
            if (StringComparer.Ordinal.Equals(context.CompletedChildExecutableNodeId, LateChildNodeId))
                return ValueTask.FromResult(RuntimeStructuralContinuation.Complete());

            var runtimeContext = (IRuntimeActivityExecutionContext)context.ParentContext;
            runtimeContext.RequestChildSubtreeCancellation(directive.TargetActivityExecutionId, directive.Reason);
            if (directive.StageTwice)
                runtimeContext.RequestChildSubtreeCancellation(directive.TargetActivityExecutionId, directive.Reason);
            if (directive.FaultInsteadOfComplete)
                return ValueTask.FromResult(RuntimeStructuralContinuation.Faulted(new ActivityFault("test.fault", "fault with staged cancellation")));
            if (directive.DeferAndScheduleLateChild)
            {
                runtimeContext.ScheduleChildActivity(LateChildNodeId, runtimeContext.ActivityExecutionState.InvocationId);
                return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
            }

            return ValueTask.FromResult(RuntimeStructuralContinuation.Complete());
        }
    }

    /// <summary>Illegally stages a cancellation during initial structural execution (spec 112 FR-2).</summary>
    public sealed class InitialCancellationStructuralActivity(SubtreeCancellationDirective directive) : StructuralActivity,
        IRuntimeStructuralActivity,
        IRuntimeActivityChildCompletionHandler
    {
        public ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext context)
        {
            var child = Assert.Single(Assert.Single(context.ExecutableNode.ChildSlots).Activities);
            context.ScheduleChildActivity(child.ExecutableNodeId, context.ActivityExecutionState.InvocationId);
            context.RequestChildSubtreeCancellation(directive.TargetActivityExecutionId, directive.Reason);
            return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
        }

        public ValueTask<RuntimeStructuralContinuation> OnChildCompletedAsync(ActivityChildCompletedContext context) =>
            ValueTask.FromResult(RuntimeStructuralContinuation.Complete());
    }
}
