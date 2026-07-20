using System.Text.Json;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// Shared graph builders and structural test activities for the spec 112/115 structural-evaluation
/// tests (child-subtree cancellation, child-fault absorption).
/// </summary>
internal static class StructuralExecutionTestSupport
{
    public const string ChildSlotName = "Test.Children";

    public static WorkflowExecutable NewExecutable(ExecutableNode root, IReadOnlyCollection<string>? waitingNodeIds = null) =>
        new(
            identity: WorkflowExecutionHarness.Identity,
            rootActivity: root,
            resumeTargets: (waitingNodeIds ?? []).ToDictionary(
                nodeId => $"resume:{nodeId}:{WaitingActivity.ResumeTargetKey}",
                nodeId => new WorkflowExecutableResumeTarget(
                    $"resume:{nodeId}:{WaitingActivity.ResumeTargetKey}",
                    nodeId,
                    "ResumeAsync",
                    new Dictionary<string, string>(),
                    LocalResumeTargetId: WaitingActivity.ResumeTargetKey),
                StringComparer.Ordinal),
            createdAt: new DateTimeOffset(2026, 6, 12, 12, 0, 0, TimeSpan.Zero),
            compatibilityMetadata: new Dictionary<string, string>());

    public static ExecutableNode NewStructuralNode(string nodeId, Type activityType, params ExecutableNode[] children) =>
        new(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: activityType.FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: "test/structural",
            descriptorPayload: JsonSerializer.SerializeToElement(new { type = "structural" }),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            metadata: new Dictionary<string, string>(),
            childSlots: [new ExecutableChildSlot(ChildSlotName, children)]);

    public static ExecutableNode NewWaitingNode(string nodeId) =>
        new(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: typeof(WaitingActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: "test/waiting",
            descriptorPayload: JsonSerializer.SerializeToElement(new { type = "waiting" }),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            metadata: new Dictionary<string, string>());
}

public sealed record WaitState(string Marker);

public sealed record WaitTrigger(bool Released);

/// <summary>A leaf that suspends on a bookmark and only completes when externally resumed.</summary>
public sealed class WaitingActivity : StatefulActivity<ActivityUnit, WaitState, WaitTrigger>
{
    public const string ResumeTargetKey = "wait";

    protected override ValueTask<ActivityTransition<ActivityUnit, WaitState>> ExecuteAsync(ActivityExecutionContext context) =>
        ValueTask.FromResult(Suspend(
            new WaitState("waiting"),
            [new ActivityTriggerRegistration<WaitTrigger>(ResumeTargetKey, "TestWait", "wait:42")]));

    protected override ValueTask<ActivityTransition<ActivityUnit, WaitState>> ResumeAsync(ActivityResumeContext<WaitState, WaitTrigger> context) =>
        ValueTask.FromResult(Complete(ActivityUnit.Value, ActivityOutcomes.Done));
}

/// <summary>Wraps one child; absorbs nothing (defers) on child fault so a blocking incident stays put.</summary>
public sealed class PassthroughStructuralActivity : StructuralActivity,
    IRuntimeStructuralActivity,
    IRuntimeActivityChildCompletionHandler,
    IRuntimeActivityChildFaultHandler
{
    public ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext context)
    {
        var child = Assert.Single(Assert.Single(context.ExecutableNode.ChildSlots).Activities);
        context.ScheduleChildActivity(child.ExecutableNodeId, context.ActivityExecutionState.InvocationId);
        return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
    }

    public ValueTask<RuntimeStructuralContinuation> OnChildCompletedAsync(ActivityChildCompletedContext context) =>
        ValueTask.FromResult(RuntimeStructuralContinuation.Complete());

    public ValueTask<RuntimeStructuralContinuation> OnChildFaultedAsync(ActivityChildFaultedContext context) =>
        ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
}

/// <summary>Schedules all slot children; a child fault re-faults the composite (Flowchart #308 style).</summary>
public sealed class RefaultingForkActivity : StructuralActivity,
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

    public ValueTask<RuntimeStructuralContinuation> OnChildCompletedAsync(ActivityChildCompletedContext context) =>
        ValueTask.FromResult(RuntimeStructuralContinuation.Defer);

    public ValueTask<RuntimeStructuralContinuation> OnChildFaultedAsync(ActivityChildFaultedContext context) =>
        ValueTask.FromResult(RuntimeStructuralContinuation.Faulted(new ActivityFault("test.child.faulted", "child faulted; refaulting composite")));
}
