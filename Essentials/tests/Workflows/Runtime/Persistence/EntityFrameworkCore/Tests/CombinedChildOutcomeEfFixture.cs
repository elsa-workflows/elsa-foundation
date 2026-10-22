using System.Collections.Concurrent;
using System.Text.Json;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

internal static class CombinedChildOutcomeEfFixture
{
    public const string WorkflowExecutionId = "wfexec-combined-child-outcome";
    public const string ParentActivityExecutionId = "actexec-combined-parent";
    public const string CompositeActivityExecutionId = "actexec-combined-child";
    public const string WaitingActivityExecutionId = "actexec-combined-wait";
    public const string FaultingActivityExecutionId = "actexec-combined-fault";
    public const string AfterActivityExecutionId = "actexec-combined-after";

    public const string ParentNodeId = "node-combined-parent";
    public const string CompositeNodeId = "node-combined-child";
    public const string WaitingNodeId = "node-combined-wait";
    public const string FaultingNodeId = "node-combined-fault";
    public const string AfterNodeId = "node-combined-after";

    public const string AfterEventName = "combined-outcome-after-resume";
    public const string FirstNotificationCode = "test.signal.first";
    public const string SecondNotificationCode = "test.signal.second";
    public const string Secret = "private-outcome-detail";
    public const string SecretReference = "combined-outcome";
    public const string SecretMarker = "[secret:combined-outcome]";
    public const string FaultCode = "test.intentional-composite-fault";
    public const string ChildSlotName = "CombinedOutcome.Children";

    public static WorkflowExecutable CreateExecutable()
    {
        var root = NewStructuralNode(
            ParentNodeId,
            typeof(CombinedOutcomeParentActivity),
            NewStructuralNode(
                CompositeNodeId,
                typeof(FaultingNotifyingCompositeActivity),
                NewEventNode(WaitingNodeId, "combined-outcome-never-resumed"),
                WorkflowExecutionHarness.NewFaultingNode(FaultingNodeId, "descendant fault")),
            NewEventNode(AfterNodeId, AfterEventName));

        var waitingResumeTargetId = WorkflowExecutableResumeTarget.ComposeScopedId(WaitingNodeId, Event.ResumeTargetId);
        var afterResumeTargetId = WorkflowExecutableResumeTarget.ComposeScopedId(AfterNodeId, Event.ResumeTargetId);
        return new WorkflowExecutable(
            WorkflowExecutionHarness.Identity,
            root,
            new Dictionary<string, WorkflowExecutableResumeTarget>(StringComparer.Ordinal)
            {
                [waitingResumeTargetId] = new(waitingResumeTargetId, WaitingNodeId, "ResumeAsync", new Dictionary<string, string>(), Event.ResumeTargetId),
                [afterResumeTargetId] = new(afterResumeTargetId, AfterNodeId, "ResumeAsync", new Dictionary<string, string>(), Event.ResumeTargetId)
            },
            WorkflowExecutionHarness.Timestamp,
            new Dictionary<string, string>(),
            IncidentStrategyBuiltIns.FaultReference);
    }

    public static string[] ActivityExecutionIds =>
    [
        ParentActivityExecutionId,
        CompositeActivityExecutionId,
        WaitingActivityExecutionId,
        FaultingActivityExecutionId,
        AfterActivityExecutionId
    ];

    private static ExecutableNode NewStructuralNode(string nodeId, Type activityType, params ExecutableNode[] children) =>
        new(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: activityType.FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: "test/structural",
            descriptorPayload: JsonSerializer.SerializeToElement(new { type = "structural" }),
            inputBindings: activityType == typeof(FaultingNotifyingCompositeActivity)
                ? new Dictionary<string, RuntimeInputBinding>
                {
                    [nameof(FaultingNotifyingCompositeActivity.Token)] = new(
                        nameof(FaultingNotifyingCompositeActivity.Token),
                        new ValueTypeDescriptor("String"),
                        SecretBindingTestSupport.SecretPolicy,
                        RuntimeInputBindingSource.SecretRead,
                        conversionPlan: ValueConversionPlan.Identity(new ValueTypeDescriptor("String"), ValueRepresentation.TextValue),
                        secret: new RuntimeSecretReference(SecretReference, "text"))
                }
                : new Dictionary<string, RuntimeInputBinding>(),
            metadata: new Dictionary<string, string>(),
            childSlots: [new ExecutableChildSlot(ChildSlotName, children)]);

    private static ExecutableNode NewEventNode(string nodeId, string eventName)
    {
        using var descriptor = JsonDocument.Parse("""{"type":"combined-outcome-wait"}""");
        return new ExecutableNode(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: Event.ActivityType,
            activityTypeVersion: "1.0.0",
            descriptorType: "test",
            descriptorPayload: descriptor.RootElement.Clone(),
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                [nameof(Event.EventName)] = RuntimeEntityFrameworkCoreEndToEndTests.Literal(nameof(Event.EventName), "String", eventName),
                [nameof(Event.CanStartWorkflow)] = RuntimeEntityFrameworkCoreEndToEndTests.Literal(nameof(Event.CanStartWorkflow), "Boolean", false)
            },
            metadata: new Dictionary<string, string>());
    }
}

public sealed record CombinedOutcomeNotificationSnapshot(string Code, string? Payload);

public sealed record CombinedOutcomeSnapshot(
    string ChildActivityExecutionId,
    string ChildExecutableNodeId,
    string? ChildIterationId,
    string FaultCode,
    string FaultMessage,
    string IncidentId,
    IReadOnlyList<CombinedOutcomeNotificationSnapshot> Notifications);

public sealed class CombinedOutcomeObservation
{
    private readonly ConcurrentQueue<CombinedOutcomeSnapshot> _outcomes = new();

    public IReadOnlyList<CombinedOutcomeSnapshot> Outcomes => _outcomes.ToArray();

    public void Record(ActivityChildOutcomeContext context) =>
        _outcomes.Enqueue(new CombinedOutcomeSnapshot(
            context.FaultedChildActivityExecutionId,
            context.FaultedChildExecutableNodeId,
            context.FaultedChildIterationId,
            context.Fault.Code,
            context.Fault.Message,
            context.IncidentId,
            context.Notifications.Select(notification => new CombinedOutcomeNotificationSnapshot(
                notification.Code,
                notification.Payload?.GetRawText())).ToArray()));
}

internal sealed record CombinedOutcomeSchedulerDispatch(string WorkItemId, SchedulerCompletionKind? CompletionKind, WorkflowExecutionCommandKind CommandKind);

internal sealed class CombinedOutcomeOutboxProbe
{
    private readonly ConcurrentQueue<CombinedOutcomeSchedulerDispatch> _attempts = new();
    private int _failNextOutcome;

    public IReadOnlyList<CombinedOutcomeSchedulerDispatch> Attempts => _attempts.ToArray();

    public void FailNextParentOutcome() => Interlocked.Exchange(ref _failNextOutcome, 1);

    public bool TryFailParentOutcome() => Interlocked.Exchange(ref _failNextOutcome, 0) == 1;

    public void Record(RuntimePostCommitIntent intent)
    {
        if (!StringComparer.Ordinal.Equals(intent.Kind, RuntimePostCommitIntentKinds.EnqueueSchedulerWork) || intent.Payload is not { } payload)
            return;

        var workItem = payload.Deserialize<RuntimeSchedulerWorkItem>();
        if (workItem is null)
            return;

        SchedulerCompletionKind? completionKind = null;
        if (workItem.CommandKind == WorkflowExecutionCommandKind.CompleteActivity &&
            workItem.Payload is { } workPayload &&
            workPayload.Deserialize<RuntimeCompleteActivityCommandPayload>() is { } completePayload)
            completionKind = completePayload.CompletionKind;

        _attempts.Enqueue(new CombinedOutcomeSchedulerDispatch(workItem.WorkItemId, completionKind, workItem.CommandKind));
    }
}

internal sealed class FailFirstCombinedOutcomeDispatcher(
    IRuntimePostCommitIntentDispatcher inner,
    CombinedOutcomeOutboxProbe probe) : IRuntimePostCommitIntentDispatcher
{
    public ValueTask DispatchAsync(RuntimePostCommitIntent intent, CancellationToken cancellationToken = default)
    {
        probe.Record(intent);
        if (intent.Kind == RuntimePostCommitIntentKinds.EnqueueSchedulerWork &&
            intent.Payload is { } payload &&
            payload.Deserialize<RuntimeSchedulerWorkItem>() is { } workItem &&
            workItem.CommandKind == WorkflowExecutionCommandKind.CompleteActivity &&
            workItem.Payload is { } workPayload &&
            workPayload.Deserialize<RuntimeCompleteActivityCommandPayload>()?.CompletionKind == SchedulerCompletionKind.ParentOutcomeEvaluation &&
            probe.TryFailParentOutcome())
        {
            throw new TimeoutException("The test loses the first combined parent-outcome enqueue before queue acceptance.");
        }

        return inner.DispatchAsync(intent, cancellationToken);
    }
}

public sealed class CombinedOutcomeParentActivity(CombinedOutcomeObservation observation) : StructuralActivity,
    IRuntimeStructuralActivity,
    IRuntimeActivityChildOutcomeHandler,
    IRuntimeActivityChildCompletionHandler
{
    public ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext context)
    {
        var child = context.ExecutableNode.ChildSlots.Single().Activities.First();
        context.ScheduleChildActivity(child.ExecutableNodeId, context.ActivityExecutionState.InvocationId);
        return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
    }

    public ValueTask<RuntimeStructuralContinuation> OnChildOutcomeAsync(ActivityChildOutcomeContext context)
    {
        observation.Record(context);
        var runtimeContext = (IRuntimeActivityExecutionContext)context.ParentContext;
        runtimeContext.RequestChildFaultAbsorption(context.IncidentId, "ef-combined-outcome-accepted");
        runtimeContext.ScheduleChildActivity(
            CombinedChildOutcomeEfFixture.AfterNodeId,
            runtimeContext.ActivityExecutionState.InvocationId);
        return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
    }

    public ValueTask<RuntimeStructuralContinuation> OnChildCompletedAsync(ActivityChildCompletedContext context) =>
        ValueTask.FromResult(RuntimeStructuralContinuation.Complete());
}

public sealed class FaultingNotifyingCompositeActivity : StructuralActivity,
    IRuntimeStructuralActivity,
    IRuntimeActivityChildFaultHandler
{
    [ActivityInput]
    public string Token { get; set; } = null!;

    public ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext context)
    {
        foreach (var child in context.ExecutableNode.ChildSlots.Single().Activities)
            context.ScheduleChildActivity(child.ExecutableNodeId, context.ActivityExecutionState.InvocationId);
        return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
    }

    public ValueTask<RuntimeStructuralContinuation> OnChildFaultedAsync(ActivityChildFaultedContext context)
    {
        var runtimeContext = (IRuntimeActivityExecutionContext)context.ParentContext;
        runtimeContext.RequestParentNotification(
            CombinedChildOutcomeEfFixture.FirstNotificationCode,
            JsonSerializer.SerializeToElement(new { ordinal = 1, value = "first" }));
        runtimeContext.RequestParentNotification(
            CombinedChildOutcomeEfFixture.SecondNotificationCode,
            JsonSerializer.SerializeToElement(new { ordinal = 2, value = "second" }));
        return ValueTask.FromResult(RuntimeStructuralContinuation.Faulted(new ActivityFault(
            CombinedChildOutcomeEfFixture.FaultCode,
            $"refused {Token}")));
    }
}


// Both sweep scopes wait here immediately before RuntimePostCommitOutboxProcessor enters its durable ClaimAsync call.
internal sealed class CombinedOutcomeRetryClaimBarrier
{
    private readonly TaskCompletionSource _bothClaimersReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _arrivals;

    public int Arrivals => Volatile.Read(ref _arrivals);

    public async ValueTask WaitForBothClaimersAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Increment(ref _arrivals) >= 2)
            _bothClaimersReady.TrySetResult();

        await _bothClaimersReady.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
    }
}


internal sealed class CombinedOutcomeClaimBarrierOutboxProcessor(
    IRuntimePostCommitOutboxProcessor inner,
    CombinedOutcomeRetryClaimBarrier barrier) : IRuntimePostCommitOutboxProcessor
{
    public async ValueTask<RuntimePostCommitOutboxProcessResult> ProcessAsync(
        RuntimePostCommitOutboxProcessRequest request,
        CancellationToken cancellationToken = default)
    {
        if (request.WorkflowExecutionId is null && request.IntentKind is null)
            await barrier.WaitForBothClaimersAsync(cancellationToken);

        return await inner.ProcessAsync(request, cancellationToken);
    }
}
