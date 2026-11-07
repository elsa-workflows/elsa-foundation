using System.Text.Json;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.StateMachine.M0.Witness;

// Research-only composite/result adapter. No production module references these CLR types.
public static class CauseProbeNames
{
    public const string Host = "m0-cause-host";
    public const string Machine = "m0-cause-machine";
    public const string Ambient = "m0-cause-ambient";
    public const string Owner = "m0-cause-owner";
    public const string SetOwner = "m0-cause-set-owner";
    public const string PublishControl = "m0-cause-publish-control";
    public const string PublisherA = "m0-cause-publisher-a";
    public const string PublisherB = "m0-cause-publisher-b";
    public const string Correlation = "correlation";
    public const string Slot = "M0.Cause.Children";
    public const string CommonEvent = "m0-cause-business";
    public const string WrongBindingDiagnostic = "N10: composite completion must retain the originating event ID";
    public static string Trigger(string source) => $"m0-cause-trigger-{source}";
    public static string Body(string source) => $"m0-cause-body-{source}";
    public static string Event(string source) => $"m0-cause-event-{source}";
    public static string FirstRelay(string source) => $"m0-cause-first-relay-{source}";
    public static string Gate(string source) => $"m0-cause-gate-{source}";
    public static string FinalRelay(string source) => $"m0-cause-final-relay-{source}";

    public static string InputId(JsonElement payload) => payload.ValueKind == JsonValueKind.Object &&
        payload.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(id.GetString())
            ? id.GetString()! : throw new InvalidOperationException("Cause probe requires an explicit public input ID.");
}

public sealed record CauseBranchResult(string Source, string BoundaryInvocationId, string BodyInvocationId,
    string EventInvocationId, string InitialRelayInvocationId, string FinalRelayInvocationId,
    string OriginInputId, string FinalInputId, JsonElement FinalPayload);
public sealed record CauseReport(string MachineInvocationId, CauseBranchResult[] Branches, string[] PublisherInvocationIds,
    string? AmbientWaitInvocationId = null, bool AmbientWasSuspendedBeforeCleanup = false);
public sealed record CauseMachineState(string Phase, CauseBranchResult[] Branches, string[] Publishers);
public sealed record CauseMarkerState(string Phase);

[Activity(Icon = "Workflow")]
[ActivityStructure("m0.cause-host", "1.0.0")]
[ActivityChildSlot(CauseProbeNames.Slot, "activities", "Activities", ActivityChildSlotCardinalities.Many)]
[ActivityOutcome(ActivityOutcomes.Done)]
public sealed class CauseHostProbe(IActivityExecutionStateStore store) : ScopeStructuralProbe<CauseMarkerState, CauseReport>,
    IRuntimeLiveChildActivityConsumer
{
    public override ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext context)
    {
        context.ScheduleChildActivity(CauseProbeNames.Ambient);
        context.ScheduleChildActivity(CauseProbeNames.Machine);
        return ValueTask.FromResult(Persist(new("Waiting")));
    }

    public override async ValueTask<RuntimeStructuralContinuation> OnChildCompletedAsync(ActivityChildCompletedContext completion)
    {
        if (completion.CompletedChildExecutableNodeId != CauseProbeNames.Machine)
            throw new InvalidOperationException("Outer cause fixture received an unrelated completion.");
        var context = Context(completion);
        var report = await ReadChildResultAsync<CauseReport>(store, context, completion.CompletedChildActivityExecutionId);
        var ambient = context.GetLiveChildActivities().Single(item => item.ExecutableNodeId == CauseProbeNames.Ambient);
        if (ambient.Status != ActivityExecutionStatus.Suspended)
            throw new InvalidOperationException("The unrelated same-name wait did not remain suspended.");
        context.RequestChildSubtreeCancellation(ambient.ActivityExecutionId, "Owned cause fixture cleanup after isolation observation");
        return Finish(report with { AmbientWaitInvocationId = ambient.ActivityExecutionId, AmbientWasSuspendedBeforeCleanup = true });
    }
}

[Activity(Icon = "Workflow")]
[ActivityStructure("m0.cause-machine", "1.0.0", SupportsScopedVariables = true)]
[ActivityChildSlot(CauseProbeNames.Slot, "activities", "Activities", ActivityChildSlotCardinalities.Many)]
[ActivityOutcome(ActivityOutcomes.Done)]
public sealed class CauseMachineProbe(IActivityExecutionStateStore store) : ScopeStructuralProbe<CauseMachineState, CauseReport>
{
    public override ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext context)
    {
        context.ScheduleChildActivity(CauseProbeNames.Owner);
        return ValueTask.FromResult(Persist(new("Owner", [], [])));
    }

    public override async ValueTask<RuntimeStructuralContinuation> OnChildCompletedAsync(ActivityChildCompletedContext completion)
    {
        var context = Context(completion);
        var state = Read(context);
        switch (completion.CompletedChildExecutableNodeId)
        {
            case CauseProbeNames.Owner:
                if (state.Phase != "Owner") throw new InvalidOperationException("Unexpected owner completion.");
                context.ScheduleChildActivity(CauseProbeNames.SetOwner, completion.CompletedChildActivityExecutionId);
                return Persist(state with { Phase = "SetOwner" });
            case CauseProbeNames.SetOwner:
                if (state.Phase != "SetOwner") throw new InvalidOperationException("Unexpected correlation assignment.");
                context.ScheduleChildActivity(CauseProbeNames.Trigger("child"));
                context.ScheduleChildActivity(CauseProbeNames.Trigger("parent"));
                context.ScheduleChildActivity(CauseProbeNames.PublishControl);
                return Persist(state with { Phase = "Armed" });
            case CauseProbeNames.PublishControl:
                if (state.Phase != "Armed") throw new InvalidOperationException("Publication was not explicitly armed.");
                context.ScheduleChildActivity(CauseProbeNames.PublisherA);
                if (Distinct(context)) context.ScheduleChildActivity(CauseProbeNames.PublisherB);
                return Persist(state with { Phase = "Published" });
            case CauseProbeNames.PublisherA:
            case CauseProbeNames.PublisherB:
                if (state.Phase != "Published") throw new InvalidOperationException("Publisher completed outside the probe phase.");
                state = state with { Publishers = [.. state.Publishers, completion.CompletedChildActivityExecutionId] };
                break;
            default:
                if (state.Phase != "Published" || completion.CompletedChildExecutableNodeId is not ("m0-cause-trigger-child" or "m0-cause-trigger-parent"))
                    throw new InvalidOperationException("Cause machine received an unrelated completion.");
                var branch = await ReadChildResultAsync<CauseBranchResult>(store, context, completion.CompletedChildActivityExecutionId);
                var expectedSource = completion.CompletedChildExecutableNodeId == CauseProbeNames.Trigger("child") ? "child" : "parent";
                if (branch.Source != expectedSource || branch.BoundaryInvocationId != completion.CompletedChildActivityExecutionId ||
                    state.Branches.Any(item => item.Source == branch.Source))
                    throw new InvalidOperationException("Cause branch evidence does not belong to this exact occurrence.");
                state = state with { Branches = [.. state.Branches, branch] };
                break;
        }
        // This join collects provenance evidence. It is not a proposed StateMachine
        // eligibility rule for partially completed arbitrary trigger work.
        return state.Branches.Length == 2 && state.Publishers.Length == (Distinct(context) ? 2 : 1)
            ? Finish(new(context.ActivityExecutionState.InvocationId, state.Branches, state.Publishers))
            : Persist(state);
    }

    private static bool Distinct(IRuntimeActivityExecutionContext context) =>
        context.ExecutableNode.Structure!.Payload.GetProperty("distinct").GetBoolean();
}

[Activity(Icon = "Workflow")]
[ActivityStructure("m0.cause-trigger", "1.0.0")]
[ActivityChildSlot(CauseProbeNames.Slot, "activities", "Activities", ActivityChildSlotCardinalities.Many)]
[ActivityOutcome(ActivityOutcomes.Done)]
public sealed class CauseTriggerProbe(IActivityExecutionStateStore store) : ScopeStructuralProbe<CauseMarkerState, CauseBranchResult>
{
    public override ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext context)
    {
        var source = Source(context);
        context.ScheduleChildActivity(CauseProbeNames.Body(source));
        return ValueTask.FromResult(Persist(new("Body")));
    }

    public override async ValueTask<RuntimeStructuralContinuation> OnChildCompletedAsync(ActivityChildCompletedContext completion)
    {
        var context = Context(completion);
        var source = Source(context);
        if (completion.CompletedChildExecutableNodeId != CauseProbeNames.Body(source))
            throw new InvalidOperationException("Cause boundary received a different composite's completion.");
        var body = await store.FindAsync(context.WorkflowExecutionId, completion.CompletedChildActivityExecutionId)
            ?? throw new InvalidOperationException("Completed cause composite is absent.");
        if (body.ParentActivityExecutionId != context.ActivityExecutionState.InvocationId || body.Status != ActivityExecutionStatus.Completed)
            throw new InvalidOperationException("Cause composite must be this boundary's exact completed child.");
        var children = (await store.ListAllAsync(context.WorkflowExecutionId)).Where(item => item.ParentActivityExecutionId == body.InvocationId).ToArray();
        ActivityExecutionState Child(string node)
        {
            var child = children.Single(item => item.Execution.ExecutableNodeId == node);
            if (child.Status != ActivityExecutionStatus.Completed) throw new InvalidOperationException("Cause evidence requires actual ordinary completion.");
            return child;
        }
        var wait = Child(CauseProbeNames.Event(source));
        var first = Child(CauseProbeNames.FirstRelay(source));
        var final = Child(CauseProbeNames.FinalRelay(source));
        var origin = wait.Completion?.Result.InlineValue?.Deserialize<EventResult>(ProbeNames.Json)?.Payload
            ?? throw new InvalidOperationException("Originating Event has no ordinary payload result.");
        var firstPayload = first.Completion?.Result.InlineValue ?? throw new InvalidOperationException("First relay has no ordinary result.");
        var finalPayload = final.Completion?.Result.InlineValue ?? throw new InvalidOperationException("Final relay has no ordinary result.");
        if (!JsonElement.DeepEquals(firstPayload, origin))
            throw new InvalidOperationException("The first relay did not preserve the Event payload.");
        return Finish(new(source, context.ActivityExecutionState.InvocationId, body.InvocationId, wait.InvocationId,
            first.InvocationId, final.InvocationId, CauseProbeNames.InputId(origin), CauseProbeNames.InputId(finalPayload), finalPayload.Clone()));
    }

    private static string Source(IRuntimeActivityExecutionContext context) => context.ExecutableNode.ExecutableNodeId switch
    {
        "m0-cause-trigger-child" => "child",
        "m0-cause-trigger-parent" => "parent",
        _ => throw new InvalidOperationException("Unknown trigger boundary source.")
    };
}

[Activity(Icon = "Link")]
[ActivityOutcome(ActivityOutcomes.Done)]
public sealed class CauseOwnerProbe(IActivityExecutionStateStore store) : Elsa.Activities.Runtime.Core.Models.Activity<string>
{
    protected override async ValueTask<ActivityTransition<string>> ExecuteAsync(ActivityExecutionContext context)
    {
        var self = await store.FindAsync(context.WorkflowExecutionId, context.InvocationId)
            ?? throw new InvalidOperationException("Owner helper invocation is absent.");
        var owner = self.ParentActivityExecutionId is { } parentId ? await store.FindAsync(context.WorkflowExecutionId, parentId) : null;
        if (owner?.Execution.ExecutableNodeId != CauseProbeNames.Machine || owner.Status != ActivityExecutionStatus.Running)
            throw new InvalidOperationException("The correlation helper needs its actual live machine parent.");
        return ActivityTransition.Complete(owner.InvocationId);
    }
}

[Activity(Icon = "Link")]
[ActivityOutcome(ActivityOutcomes.Done)]
public sealed class CausePayloadRelay : Elsa.Activities.Runtime.Core.Models.Activity<JsonElement>
{
    [ActivityInput] public JsonElement Payload { get; set; }
    protected override ValueTask<ActivityTransition<JsonElement>> ExecuteAsync(ActivityExecutionContext context)
    {
        _ = CauseProbeNames.InputId(Payload);
        return ValueTask.FromResult(ActivityTransition.Complete(Payload.Clone()));
    }
}
