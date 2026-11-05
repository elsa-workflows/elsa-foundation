using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.StateMachine.M0.Witness;

// Experimental fixture only. No production module references this assembly.
public static class ProbeNames
{
    public const string Root = "m0-root";
    public const string Exit = "m0-exit";
    public const string Action = "m0-action";
    public const string Inbox = "m0-inbox";
    public const string ChildGuard = "m0-child-guard";
    public const string ParentGuard = "m0-parent-guard";
    public const string Slot = "M0.Children";
    public const string InputId = "m0.input-id";
    public const string Source = "m0.source";
    public const string Invocation = "m0.machine-invocation";
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };
}

public sealed record ProbeInput(string Id, JsonElement Content, bool ParentFirst = false);
public sealed record ProbeReceipt(string Id, JsonElement Content, long Order, string Status, string CarrierInvocationId);
public sealed record ProbeDecision(string CarrierInvocationId, string InputId, string Status, long? Order);
public sealed record ProbeCallback(string InvocationId, string InputId, string Source, string OperationKey);
public sealed record ProbeSelection(string InputId, string Source);
public sealed record ProbeFinalReceipt([property: JsonPropertyName("id")] string Id, [property: JsonPropertyName("order")] long Order,
    [property: JsonPropertyName("status")] string Status, [property: JsonPropertyName("carrierInvocationId")] string CarrierInvocationId);
public sealed record ProbeTerminalEvidence([property: JsonPropertyName("formatVersion")] int FormatVersion,
    [property: JsonPropertyName("rootInvocationId")] string RootInvocationId, [property: JsonPropertyName("reason")] string Reason,
    [property: JsonPropertyName("receipts")] ProbeFinalReceipt[] Receipts);
public sealed record ProbeState(
    string Phase,
    int ExitCompletions,
    long NextOrder,
    List<string> Pending,
    Dictionary<string, ProbeReceipt> Receipts,
    List<ProbeDecision> Decisions,
    string? InFlight,
    List<string> ReadySources,
    List<ProbeCallback> Callbacks,
    List<ProbeSelection> Selections,
    List<string> Processed,
    bool AwaitingRecovery,
    string? AwaitedNode,
    string? FailedChild,
    string? Incident)
{
    public static ProbeState Initial() => new("Exit", 0, 0, [], new(StringComparer.Ordinal), [], null,
        [], [], [], [], false, null, null, null);
}

public sealed class ProbeControls
{
    public string? FailGuardForInput { get; init; }
    public bool OmitPrivateState { get; init; }
    public bool CorruptProvenance { get; init; }
    public bool DisableTerminalReceiptExport { get; init; }
    public bool DisableCancellationProjection { get; init; }
    public bool IgnoreInFlightCapacity { get; init; }
    private int _guardFailed;

    public bool ShouldFailGuard(string input) => input == FailGuardForInput && Interlocked.Exchange(ref _guardFailed, 1) == 0;
}

[Activity(Icon = "Workflow")]
[ActivityStructure("m0.native-probe", "1.0.0", Mode = "m0-probe")]
[ActivityChildSlot(ProbeNames.Slot, "activities", "Activities", ActivityChildSlotCardinalities.Many)]
[ActivityOutcome(ActivityOutcomes.Done)]
public abstract class NativeProbeBase(IActivityExecutionStateStore activities, ProbeControls controls) : Activity<ProbeTerminalEvidence>,
    IRuntimeStructuralActivity, IRuntimeActivityChildCompletionHandler, IRuntimeActivityChildFaultHandler,
    IRuntimeLiveChildActivityConsumer, IRuntimeActivityCheckpointParticipant
{
    public const int PendingCapacity = 2;
    public const int ReceiptBudget = 8;
    public const int AttemptHistoryBudget = 16;
    // Same-activation decision handoff to the immediate checkpoint participant; never recovery state.
    private ProbeTerminalEvidence? _terminalEvidence;

    protected override ValueTask<ActivityTransition<ProbeTerminalEvidence>> ExecuteAsync(ActivityExecutionContext context) =>
        throw new InvalidOperationException("The native M0 structural fixture refuses ordinary invocation.");

    public ValueTask<IReadOnlyCollection<RuntimeStateChange<DurableValueState>>> PrepareEntryCheckpointAsync(
        IRuntimeActivityExecutionContext context, IReadOnlyDictionary<string, object?> effectiveInputs, DateTimeOffset capturedAt,
        CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyCollection<RuntimeStateChange<DurableValueState>>>([]);

    public ValueTask<RuntimeActivityCompletionCheckpointPreparation> PrepareCompletionCheckpointAsync(
        IRuntimeActivityExecutionContext context, IReadOnlyCollection<DurableValueState> persistedValues, DateTimeOffset capturedAt,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var evidence = _terminalEvidence ?? throw new InvalidOperationException("No same-activation terminal decision exists.");
        if (controls.DisableTerminalReceiptExport) evidence = evidence with { Receipts = [] };
        return ValueTask.FromResult(new RuntimeActivityCompletionCheckpointPreparation([], ActivityTransition.Complete(evidence)));
    }

    public ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext context)
    {
        context.ScheduleChildActivity(ProbeNames.Exit);
        context.ScheduleChildActivity(ProbeNames.Inbox);
        return ValueTask.FromResult(Persist(RuntimeStructuralContinuation.Defer, ProbeState.Initial()));
    }

    public async ValueTask<RuntimeStructuralContinuation> OnChildCompletedAsync(ActivityChildCompletedContext completion)
    {
        var context = (IRuntimeActivityExecutionContext)completion.ParentContext;
        var state = Read(context.ActivityExecutionState);
        var node = completion.CompletedChildExecutableNodeId;
        if (state.AwaitingRecovery && state.AwaitedNode == node)
            state = state with { AwaitingRecovery = false, AwaitedNode = null };

        if (node == ProbeNames.Exit)
        {
            if (state.Phase != "Exit") throw new InvalidOperationException("Exit completed in the wrong phase.");
            context.ScheduleChildActivity(ProbeNames.Action);
            return Persist(RuntimeStructuralContinuation.Defer, state with { Phase = "Action", ExitCompletions = state.ExitCompletions + 1 });
        }

        if (node == ProbeNames.Action)
        {
            var child = await activities.FindAsync(context.WorkflowExecutionId, completion.CompletedChildActivityExecutionId)
                ?? throw new InvalidOperationException("Completed Action is absent.");
            var signal = child.Completion?.Result.InlineValue?.Deserialize<ActionSignal>(ProbeNames.Json)
                ?? throw new InvalidOperationException("Action has no committed result.");
            if (signal.Finish) return Finish(context, state);
            return Drive(context, state with { Phase = "Idle" });
        }

        if (node == ProbeNames.Inbox)
        {
            var child = await activities.FindAsync(context.WorkflowExecutionId, completion.CompletedChildActivityExecutionId)
                ?? throw new InvalidOperationException("Carrier invocation is absent.");
            var result = child.Completion?.Result.InlineValue?.Deserialize<EventResult>(ProbeNames.Json)
                ?? throw new InvalidOperationException("Carrier has no committed result.");
            var input = result.Payload?.Deserialize<ProbeInput>(ProbeNames.Json)
                ?? throw new InvalidOperationException("Carrier has no business input.");
            if (string.IsNullOrWhiteSpace(input.Id) || input.Content.ValueKind == JsonValueKind.Undefined)
                throw new InvalidOperationException("The fixture needs an ID and public JSON content.");

            var receipts = state.Receipts.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            var pending = state.Pending.ToList();
            string status;
            long? order = null;
            if (receipts.TryGetValue(input.Id, out var existing))
            {
                status = JsonElement.DeepEquals(existing.Content, input.Content) ? "Duplicate" : "Conflict";
                order = existing.Order;
            }
            else if (pending.Count + (controls.IgnoreInFlightCapacity || state.InFlight is null ? 0 : 1) >= PendingCapacity || receipts.Count >= ReceiptBudget)
                status = "Full";
            else
            {
                status = "Accepted";
                order = state.NextOrder + 1;
                receipts.Add(input.Id, new(input.Id, input.Content.Clone(), order.Value, "Queued", child.InvocationId));
                pending.Add(input.Id);
                state = state with { NextOrder = order.Value };
            }

            var decision = new ProbeDecision(child.InvocationId, input.Id, status, order);
            var decisions = state.Decisions.Append(decision).TakeLast(AttemptHistoryBudget).ToList();
            // These are native schedule/checkpoint requests, not a private scheduler or a transport acknowledgement.
            context.ScheduleChildActivity(ProbeNames.Inbox);
            return Drive(context, state with { Pending = pending, Receipts = receipts, Decisions = decisions });
        }

        if (node is ProbeNames.ChildGuard or ProbeNames.ParentGuard)
        {
            var input = completion.ChildSchedulingMetadata.GetValueOrDefault(ProbeNames.InputId);
            var source = completion.ChildSchedulingMetadata.GetValueOrDefault(ProbeNames.Source);
            var owner = completion.ChildSchedulingMetadata.GetValueOrDefault(ProbeNames.Invocation);
            if (input != state.InFlight || owner != context.ActivityExecutionState.InvocationId || source is not ("child" or "parent"))
                throw new InvalidOperationException("Logical input provenance does not match the selected native work.");
            var child = await activities.FindAsync(context.WorkflowExecutionId, completion.CompletedChildActivityExecutionId)
                ?? throw new InvalidOperationException("Guard occurrence is absent.");
            var ready = state.ReadySources.Append(source).Distinct(StringComparer.Ordinal).ToList();
            var callbacks = state.Callbacks.Append(new ProbeCallback(child.InvocationId, input!, source, child.OperationKey)).ToList();
            state = state with { ReadySources = ready, Callbacks = callbacks };
            if (ready.Count != 2) return Persist(RuntimeStructuralContinuation.Defer, state);

            var receipts = state.Receipts.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            receipts[input!] = receipts[input!] with { Status = "Handled" };
            var processed = state.Processed.Append(input!).ToList();
            // Both ordinary callbacks have the same cause. This minimal fixture selects the child independently
            // of their arrival order; it is not the production statechart candidate-selection implementation.
            var selected = ready.OrderBy(candidate => candidate == "child" ? 0 : 1).First();
            state = state with { Receipts = receipts, Processed = processed, Selections = state.Selections.Append(new(input!, selected)).ToList(), InFlight = null, ReadySources = [], Phase = "Idle" };
            return input == "stop" ? Finish(context, state) : Drive(context, state);
        }

        throw new InvalidOperationException($"Unexpected child '{node}'.");
    }

    public ValueTask<RuntimeStructuralContinuation> OnChildFaultedAsync(ActivityChildFaultedContext fault)
    {
        var context = (IRuntimeActivityExecutionContext)fault.ParentContext;
        var state = Read(context.ActivityExecutionState);
        context.RequestChildFaultIntervention(fault.IncidentId ?? throw new InvalidOperationException("Fault has no incident."), "M0 fixture awaits normal retry");
        return ValueTask.FromResult(Persist(RuntimeStructuralContinuation.Defer, state with
        {
            AwaitingRecovery = true,
            AwaitedNode = fault.FaultedChildExecutableNodeId,
            FailedChild = fault.FaultedChildActivityExecutionId,
            Incident = fault.IncidentId
        }));
    }

    private RuntimeStructuralContinuation Drive(IRuntimeActivityExecutionContext context, ProbeState state)
    {
        if (state.Phase != "Idle" || state.AwaitingRecovery || state.InFlight is not null || state.Pending.Count == 0)
            return Persist(RuntimeStructuralContinuation.Defer, state);

        var id = state.Pending[0];
        var receipts = state.Receipts.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        receipts[id] = receipts[id] with { Status = "Selected" };
        state = state with { InFlight = id, Pending = state.Pending.Skip(1).ToList(), Receipts = receipts, ReadySources = [], Phase = "Guard" };
        var parentFirst = receipts[id].Content.TryGetProperty("parentFirst", out var value) && value.ValueKind == JsonValueKind.True;
        var nodes = parentFirst ? new[] { ProbeNames.ParentGuard, ProbeNames.ChildGuard } : new[] { ProbeNames.ChildGuard, ProbeNames.ParentGuard };
        foreach (var node in nodes)
            context.ScheduleChildActivity(node, metadata: new Dictionary<string, string>
            {
                [ProbeNames.InputId] = controls.CorruptProvenance ? "corrupt-input" : id,
                [ProbeNames.Source] = node == ProbeNames.ChildGuard ? "child" : "parent",
                [ProbeNames.Invocation] = context.ActivityExecutionState.InvocationId
            });
        return Persist(RuntimeStructuralContinuation.Defer, state);
    }

    private RuntimeStructuralContinuation Finish(IRuntimeActivityExecutionContext context, ProbeState state)
    {
        foreach (var child in context.GetLiveChildActivities())
            context.RequestChildSubtreeCancellation(child.ActivityExecutionId, "M0 parent completed");
        var receipts = state.Receipts.ToDictionary(pair => pair.Key,
            pair => pair.Value.Status == "Queued" ? pair.Value with { Status = "NotProcessed:Completed" } : pair.Value,
            StringComparer.Ordinal);
        _terminalEvidence = new(1, context.ActivityExecutionState.InvocationId, "Completed", FinalReceipts(receipts.Values));
        return Persist(RuntimeStructuralContinuation.Complete(), state with { Phase = "Completed", Receipts = receipts, Pending = [] });
    }

    public static ProbeFinalReceipt[] FinalReceipts(IEnumerable<ProbeReceipt> receipts) => receipts.OrderBy(x => x.Order)
        .Select(x => new ProbeFinalReceipt(x.Id, x.Order, x.Status, x.CarrierInvocationId)).ToArray();

    public static ProbeTerminalEvidence? ReadTerminalEvidence(ActivityExecutionState root, bool projectCancellation)
    {
        if (root.Status == ActivityExecutionStatus.Completed)
            return root.Completion?.Result.InlineValue?.Deserialize<ProbeTerminalEvidence>(ProbeNames.Json)
                ?? throw new InvalidOperationException("Completed root has no declared terminal result.");
        if (root.Status != ActivityExecutionStatus.Cancelled) return null;
        if (root.CompletedAt is null) throw new InvalidOperationException("Cancelled root has no committed completion timestamp.");
        var state = Read(root);
        var receipts = state.Receipts.Values.Select(receipt => !projectCancellation ? receipt : receipt.Status switch
        {
            "Queued" => receipt with { Status = "NotProcessed:Cancelled" },
            "Selected" => receipt with { Status = "Interrupted:Cancelled" },
            _ => receipt
        });
        return new(1, root.InvocationId, "Cancelled", FinalReceipts(receipts));
    }

    private RuntimeStructuralContinuation Persist(RuntimeStructuralContinuation continuation, ProbeState state) =>
        controls.OmitPrivateState ? continuation : continuation.WithState(ValueEnvelope.Inline(
            new ValueTypeDescriptor("Object"), JsonSerializer.SerializeToElement(state, ProbeNames.Json), ValueProtectionPolicy.InstanceInline));

    public static ProbeState Read(ActivityExecutionState state) =>
        state.PrivateState?.Value.InlineValue?.Deserialize<ProbeState>(ProbeNames.Json)
        ?? throw new InvalidOperationException("No committed native parent state is available.");
}

public sealed class NativeProbe(IActivityExecutionStateStore activities, ProbeControls controls)
    : NativeProbeBase(activities, controls), IRuntimeAcceptsRescheduledFaultedChild;

public sealed class NativeProbeWithoutRescheduleOptIn(IActivityExecutionStateStore activities, ProbeControls controls)
    : NativeProbeBase(activities, controls);

[Activity(Icon = "Check")]
[ActivitySideEffectProfile(SideEffectProfile.ReplaySafe)]
public sealed class ExitProbe : Activity
{
    protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context) =>
        ValueTask.FromResult(ActivityTransition.Complete(default(ActivityUnit)));
}

public sealed record ActionSignal([property: JsonPropertyName("fault")] bool Fault = false, [property: JsonPropertyName("finish")] bool Finish = false);
public sealed record ActionWaitState(string Name);

[Activity(Icon = "Clock")]
public sealed class ActionProbe : StatefulTriggerActivity<ActionSignal, ActionWaitState, ActionSignal>
{
    public const string ResumeId = "resume-target:m0-action";
    protected override ValueTask<ActivityTransition<ActionSignal, ActionWaitState>> ExecuteAsync(ActivityStartContext<ActionSignal> context) =>
        ValueTask.FromResult(Suspend(new ActionWaitState(ProbeNames.Action),
            [new ActivityTriggerRegistration<ActionSignal>(ResumeId, EventStimulus.StimulusType, EventStimulus.Hash(ProbeNames.Action))]));

    [ResumeTarget(ResumeId)]
    protected override ValueTask<ActivityTransition<ActionSignal, ActionWaitState>> ResumeAsync(ActivityResumeContext<ActionWaitState, ActionSignal> context)
    {
        if (context.Trigger.Fault) throw new InvalidOperationException("Controlled waiting Action failure.");
        return ValueTask.FromResult(Complete(context.Trigger));
    }
}

[Activity(Icon = "GitBranch")]
public sealed class GuardProbe(ProbeControls controls) : StructuralActivity, IRuntimeStructuralActivity
{
    [ActivityInput]
    public string Source { get; set; } = null!;

    public ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext context)
    {
        var input = context.ActivityExecutionState.Provenance.Metadata.GetValueOrDefault(ProbeNames.InputId)
            ?? throw new InvalidOperationException("Scheduled guard has no logical input identity.");
        if (Source == "child" && controls.ShouldFailGuard(input))
            throw new InvalidOperationException("Controlled transient guard adapter failure.");
        return ValueTask.FromResult(RuntimeStructuralContinuation.Complete());
    }
}

public sealed record ReceiptConsumption(string RootInvocationId, int ReceiptCount, string[] InputIds);

[Activity(Icon = "Check")]
[ActivitySideEffectProfile(SideEffectProfile.ReplaySafe)]
public sealed class ReceiptConsumer : Activity<ReceiptConsumption>
{
    [ActivityInput] public ProbeTerminalEvidence Evidence { get; set; } = null!;

    protected override ValueTask<ActivityTransition<ReceiptConsumption>> ExecuteAsync(ActivityExecutionContext context)
    {
        if (Evidence is null || string.IsNullOrWhiteSpace(Evidence.RootInvocationId))
            throw new InvalidOperationException("The ordinary result consumer needs the hydrated producer result.");
        return ValueTask.FromResult(ActivityTransition.Complete(new ReceiptConsumption(Evidence.RootInvocationId,
            Evidence.Receipts.Length, Evidence.Receipts.Select(x => x.Id).ToArray())));
    }
}
