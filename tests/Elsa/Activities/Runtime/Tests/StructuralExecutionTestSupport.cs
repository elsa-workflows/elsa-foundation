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

/// <summary>Collects the notifications delivered to a <see cref="NotificationConsumingParentActivity"/> for assertions (spec 126).</summary>
public sealed class ParentNotificationRecorder
{
    private readonly List<RecordedParentNotification> _notifications = [];
    public IReadOnlyList<RecordedParentNotification> Notifications => _notifications;
    public void Add(RecordedParentNotification notification) => _notifications.Add(notification);
}

public sealed record RecordedParentNotification(
    string Code,
    string? Payload,
    string NotifyingChildActivityExecutionId,
    string NotifyingChildExecutableNodeId,
    string? NotifyingChildIterationId);

/// <summary>What a <see cref="NotifyingStructuralChildActivity"/> stages toward its parent (spec 126, seam C).</summary>
public sealed record ParentNotificationDirective
{
    /// <summary>Notification codes staged during the child's initial structural execution.</summary>
    public IReadOnlyList<string> InvokeCodes { get; init; } = [];

    /// <summary>Notification code staged during the child's own child-completion evaluation (parent-completion harvest path).</summary>
    public string? CompletionCode { get; init; }

    /// <summary>Optional JSON payload attached to every staged notification.</summary>
    public JsonElement? Payload { get; init; }

    /// <summary>Return a <c>Fault</c> continuation after staging (spec 126: Fault + staged notification faults the evaluation).</summary>
    public bool FaultInsteadOfDefer { get; init; }

    /// <summary>Stage the notification from the ROOT structural activity (no parent) to exercise the root-reject rule.</summary>
    public bool StageFromRoot { get; init; }
}

/// <summary>How a <see cref="NotificationConsumingParentActivity"/> responds to a child notification (spec 126).</summary>
public sealed record ParentNotificationConsumerDirective
{
    public bool CancelNotifyingChildOnNotify { get; init; }
    public bool CompleteOnNotify { get; init; }
    public bool FaultOnNotify { get; init; }
}

/// <summary>
/// A structural child (spec 126) that stages parent notifications during its own evaluations, then schedules
/// its single leaf and defers (or faults per the directive). On the leaf's completion it optionally stages one
/// more notification and completes.
/// </summary>
public sealed class NotifyingStructuralChildActivity(ParentNotificationDirective directive) : StructuralActivity,
    IRuntimeStructuralActivity,
    IRuntimeActivityChildCompletionHandler
{
    public ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext context)
    {
        foreach (var code in directive.InvokeCodes)
            context.RequestParentNotification(code, directive.Payload);

        if (directive.FaultInsteadOfDefer)
            return ValueTask.FromResult(RuntimeStructuralContinuation.Faulted(new ActivityFault("test.notify.fault", "fault with staged notification")));

        var child = Assert.Single(Assert.Single(context.ExecutableNode.ChildSlots).Activities);
        context.ScheduleChildActivity(child.ExecutableNodeId, context.ActivityExecutionState.InvocationId);
        return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
    }

    public ValueTask<RuntimeStructuralContinuation> OnChildCompletedAsync(ActivityChildCompletedContext context)
    {
        if (directive.CompletionCode is { } code)
            ((IRuntimeActivityExecutionContext)context.ParentContext).RequestParentNotification(code, directive.Payload);
        return ValueTask.FromResult(RuntimeStructuralContinuation.Complete());
    }
}

/// <summary>
/// The ROOT structural activity variant that stages a notification during its own initial execution — it has no
/// committed parent, so the runtime rejects the staging (spec 126 root-reject rule).
/// </summary>
public sealed class RootNotifyingStructuralActivity(ParentNotificationDirective directive) : StructuralActivity,
    IRuntimeStructuralActivity,
    IRuntimeActivityChildCompletionHandler
{
    public ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext context)
    {
        context.RequestParentNotification(directive.InvokeCodes.Count > 0 ? directive.InvokeCodes[0] : "escalate", directive.Payload);
        var child = Assert.Single(Assert.Single(context.ExecutableNode.ChildSlots).Activities);
        context.ScheduleChildActivity(child.ExecutableNodeId, context.ActivityExecutionState.InvocationId);
        return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
    }

    public ValueTask<RuntimeStructuralContinuation> OnChildCompletedAsync(ActivityChildCompletedContext context) =>
        ValueTask.FromResult(RuntimeStructuralContinuation.Complete());
}

/// <summary>
/// A structural parent (spec 126) that consumes a child notification: it records the delivered code/payload/child
/// identity and, per its directive, defers (keeps running), completes, faults, or stages a seam-A subtree
/// cancellation of the notifying child (interrupting escalation). Completes when its child completes.
/// </summary>
public sealed class NotificationConsumingParentActivity(ParentNotificationRecorder recorder, ParentNotificationConsumerDirective directive) : StructuralActivity,
    IRuntimeStructuralActivity,
    IRuntimeActivityChildCompletionHandler,
    IRuntimeActivityChildNotificationHandler,
    IRuntimeLiveChildActivityConsumer
{
    public ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext context)
    {
        var child = Assert.Single(Assert.Single(context.ExecutableNode.ChildSlots).Activities);
        context.ScheduleChildActivity(child.ExecutableNodeId, context.ActivityExecutionState.InvocationId);
        return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
    }

    public ValueTask<RuntimeStructuralContinuation> OnChildCompletedAsync(ActivityChildCompletedContext context) =>
        ValueTask.FromResult(RuntimeStructuralContinuation.Complete());

    public ValueTask<RuntimeStructuralContinuation> OnChildNotifiedAsync(IRuntimeActivityExecutionContext context, ActivityChildNotifiedContext notification)
    {
        recorder.Add(new RecordedParentNotification(
            notification.Code,
            notification.Payload?.GetRawText(),
            notification.NotifyingChildActivityExecutionId,
            notification.NotifyingChildExecutableNodeId,
            notification.NotifyingChildIterationId));

        if (directive.CancelNotifyingChildOnNotify)
            context.RequestChildSubtreeCancellation(notification.NotifyingChildActivityExecutionId, "escalated");

        if (directive.FaultOnNotify)
            return ValueTask.FromResult(RuntimeStructuralContinuation.Faulted(new ActivityFault("test.notify.consumer.fault", "consumer faulted on notification")));

        return ValueTask.FromResult(directive.CompleteOnNotify
            ? RuntimeStructuralContinuation.Complete()
            : RuntimeStructuralContinuation.Defer);
    }
}

/// <summary>A parent that never implements <c>IRuntimeActivityChildNotificationHandler</c>: notifications to it are silently acked (spec 126).</summary>
public sealed class NonNotificationConsumingParentActivity : StructuralActivity,
    IRuntimeStructuralActivity,
    IRuntimeActivityChildCompletionHandler
{
    public ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext context)
    {
        var child = Assert.Single(Assert.Single(context.ExecutableNode.ChildSlots).Activities);
        context.ScheduleChildActivity(child.ExecutableNodeId, context.ActivityExecutionState.InvocationId);
        return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
    }

    public ValueTask<RuntimeStructuralContinuation> OnChildCompletedAsync(ActivityChildCompletedContext context) =>
        ValueTask.FromResult(RuntimeStructuralContinuation.Complete());
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
