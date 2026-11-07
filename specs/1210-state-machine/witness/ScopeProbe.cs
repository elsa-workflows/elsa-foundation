using System.Text.Json;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.StateMachine.M0.Witness;

// Research-only activities. Their CLR visibility permits normal activity construction;
// no product module or public StateMachine schema references them.
public static class ScopeProbeNames
{
    public const string Machine = "m0-scope-machine";
    public const string Parent = "m0-scope-parent";
    public const string Leaf = "m0-scope-leaf";
    public const string Slot = "M0.Scope.Children";
    public const string Counter = "counter";
    public const string Stage = "m0.scope.stage";
    public const string ParentOrdinal = "m0.scope.parentOrdinal";
    public static string Body(int stage) => $"m0-scope-body-{stage}";
    public static string Observer(int stage) => $"m0-scope-observer-{stage}";
    public static string Wait(int stage) => $"m0-scope-wait-{stage}";
}

public sealed record ScopeObservation(int Stage, int Machine, int Parent, int Leaf);
public sealed record ScopeVisitResult(string LeafInvocationId, string BodyInvocationId, string ObserverInvocationId, ScopeObservation Observation);
public sealed record ScopeVisitState(int Stage);
public sealed record ScopeParentState(int Ordinal, ScopeVisitResult[] Visits);
public sealed record ScopeMachineState(ScopeVisitResult[] Visits, int CompletedParents);
public sealed record ScopeReport(ScopeVisitResult[] Visits);

public abstract class ScopeStructuralProbe<TState, TResult> : Activity<TResult>,
    IRuntimeStructuralActivity, IRuntimeActivityChildCompletionHandler, IRuntimeActivityCheckpointParticipant
    where TState : class where TResult : class
{
    private TResult? _completion;

    protected override ValueTask<ActivityTransition<TResult>> ExecuteAsync(ActivityExecutionContext context) =>
        throw new InvalidOperationException("The scope research activity requires normal native structural execution.");

    public abstract ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext context);
    public abstract ValueTask<RuntimeStructuralContinuation> OnChildCompletedAsync(ActivityChildCompletedContext completion);

    protected static IRuntimeActivityExecutionContext Context(ActivityChildCompletedContext completion) =>
        completion.ParentContext as IRuntimeActivityExecutionContext ?? throw new InvalidOperationException("Native scope context is absent.");

    protected static TState Read(IRuntimeActivityExecutionContext context) =>
        context.ActivityExecutionState.PrivateState?.Value.InlineValue?.Deserialize<TState>(ProbeNames.Json)
        ?? throw new InvalidOperationException("No committed scope fixture continuation is available.");

    protected static RuntimeStructuralContinuation Persist(TState state) =>
        RuntimeStructuralContinuation.Defer.WithState(ValueEnvelope.Inline(new ValueTypeDescriptor("Object"),
            JsonSerializer.SerializeToElement(state, ProbeNames.Json), ValueProtectionPolicy.InstanceInline));

    protected RuntimeStructuralContinuation Finish(TResult result)
    {
        // Immediate same-activation handoff to the normal completion checkpoint,
        // never recovery state and never an alternate variable store.
        _completion = result;
        return RuntimeStructuralContinuation.Complete();
    }

    protected static async ValueTask<T> ReadChildResultAsync<T>(IActivityExecutionStateStore store,
        IRuntimeActivityExecutionContext context, string childInvocationId) where T : class
    {
        var child = await store.FindAsync(context.WorkflowExecutionId, childInvocationId)
            ?? throw new InvalidOperationException("Completed scope child is absent.");
        if (child.ParentActivityExecutionId != context.ActivityExecutionState.InvocationId || child.Status != ActivityExecutionStatus.Completed)
            throw new InvalidOperationException("Scope result must belong to this parent's exact completed child.");
        return child.Completion?.Result.InlineValue?.Deserialize<T>(ProbeNames.Json)
            ?? throw new InvalidOperationException("Completed scope child has no declared result.");
    }

    public ValueTask<IReadOnlyCollection<RuntimeStateChange<DurableValueState>>> PrepareEntryCheckpointAsync(
        IRuntimeActivityExecutionContext context, IReadOnlyDictionary<string, object?> effectiveInputs,
        DateTimeOffset capturedAt, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IReadOnlyCollection<RuntimeStateChange<DurableValueState>>>([]);

    public ValueTask<RuntimeActivityCompletionCheckpointPreparation> PrepareCompletionCheckpointAsync(
        IRuntimeActivityExecutionContext context, IReadOnlyCollection<DurableValueState> persistedValues,
        DateTimeOffset capturedAt, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var result = _completion ?? throw new InvalidOperationException("Scope completion has no same-activation declared result.");
        return ValueTask.FromResult(new RuntimeActivityCompletionCheckpointPreparation([], ActivityTransition.Complete(result)));
    }
}

[Activity(Icon = "Workflow")]
[ActivityStructure("m0.scope-machine", "1.0.0", SupportsScopedVariables = true)]
[ActivityChildSlot(ScopeProbeNames.Slot, "activities", "Activities", ActivityChildSlotCardinalities.Many)]
[ActivityOutcome(ActivityOutcomes.Done)]
public sealed class ScopeMachineProbe(IActivityExecutionStateStore store) : ScopeStructuralProbe<ScopeMachineState, ScopeReport>
{
    public override ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext context)
    {
        ScheduleParent(context, 1);
        return ValueTask.FromResult(Persist(new([], 0)));
    }

    public override async ValueTask<RuntimeStructuralContinuation> OnChildCompletedAsync(ActivityChildCompletedContext completion)
    {
        if (completion.CompletedChildExecutableNodeId != ScopeProbeNames.Parent)
            throw new InvalidOperationException("Machine fixture received an unrelated child.");
        var context = Context(completion);
        var before = Read(context);
        var result = await ReadChildResultAsync<ScopeReport>(store, context, completion.CompletedChildActivityExecutionId);
        var state = new ScopeMachineState([.. before.Visits, .. result.Visits], before.CompletedParents + 1);
        if (state.CompletedParents == 2) return Finish(new(state.Visits));
        if (state.CompletedParents != 1) throw new InvalidOperationException("Unexpected number of parent visits.");
        ScheduleParent(context, 2);
        return Persist(state);
    }

    private static void ScheduleParent(IRuntimeActivityExecutionContext context, int ordinal) =>
        context.ScheduleChildActivity(ScopeProbeNames.Parent, metadata: new Dictionary<string, string>
        {
            [ScopeProbeNames.ParentOrdinal] = ordinal.ToString(System.Globalization.CultureInfo.InvariantCulture)
        });
}

[Activity(Icon = "Workflow")]
[ActivityStructure("m0.scope-parent", "1.0.0", SupportsScopedVariables = true)]
[ActivityChildSlot(ScopeProbeNames.Slot, "activities", "Activities", ActivityChildSlotCardinalities.Many)]
[ActivityOutcome(ActivityOutcomes.Done)]
public sealed class ScopeParentProbe(IActivityExecutionStateStore store) : ScopeStructuralProbe<ScopeParentState, ScopeReport>
{
    public override ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext context)
    {
        var ordinal = int.Parse(context.ActivityExecutionState.Provenance.Metadata[ScopeProbeNames.ParentOrdinal], System.Globalization.CultureInfo.InvariantCulture);
        if (ordinal is not (1 or 2)) throw new InvalidOperationException("Invalid parent visit ordinal.");
        ScheduleLeaf(context, ordinal == 1 ? 1 : 3);
        return ValueTask.FromResult(Persist(new(ordinal, [])));
    }

    public override async ValueTask<RuntimeStructuralContinuation> OnChildCompletedAsync(ActivityChildCompletedContext completion)
    {
        if (completion.CompletedChildExecutableNodeId != ScopeProbeNames.Leaf)
            throw new InvalidOperationException("Parent fixture received an unrelated child.");
        var context = Context(completion);
        var before = Read(context);
        var result = await ReadChildResultAsync<ScopeVisitResult>(store, context, completion.CompletedChildActivityExecutionId);
        var visits = before.Visits.Append(result).ToArray();
        if (before.Ordinal == 1 && visits.Length == 1)
        {
            ScheduleLeaf(context, 2);
            return Persist(new(before.Ordinal, visits));
        }
        if (visits.Length != (before.Ordinal == 1 ? 2 : 1))
            throw new InvalidOperationException("Unexpected number of leaf visits.");
        return Finish(new(visits));
    }

    private static void ScheduleLeaf(IRuntimeActivityExecutionContext context, int stage) =>
        context.ScheduleChildActivity(ScopeProbeNames.Leaf, metadata: new Dictionary<string, string>
        {
            [ScopeProbeNames.Stage] = stage.ToString(System.Globalization.CultureInfo.InvariantCulture)
        });
}

[Activity(Icon = "Workflow")]
[ActivityStructure("m0.scope-leaf", "1.0.0", SupportsScopedVariables = true)]
[ActivityChildSlot(ScopeProbeNames.Slot, "activities", "Activities", ActivityChildSlotCardinalities.Many)]
[ActivityOutcome(ActivityOutcomes.Done)]
public sealed class ScopeLeafProbe(IActivityExecutionStateStore store) : ScopeStructuralProbe<ScopeVisitState, ScopeVisitResult>
{
    public override ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext context)
    {
        var stage = int.Parse(context.ActivityExecutionState.Provenance.Metadata[ScopeProbeNames.Stage], System.Globalization.CultureInfo.InvariantCulture);
        if (stage is < 1 or > 3) throw new InvalidOperationException("Invalid leaf stage.");
        context.ScheduleChildActivity(ScopeProbeNames.Body(stage));
        return ValueTask.FromResult(Persist(new(stage)));
    }

    public override async ValueTask<RuntimeStructuralContinuation> OnChildCompletedAsync(ActivityChildCompletedContext completion)
    {
        var context = Context(completion);
        var stage = Read(context).Stage;
        if (completion.CompletedChildExecutableNodeId != ScopeProbeNames.Body(stage))
            throw new InvalidOperationException("Leaf fixture received an unrelated body.");
        var body = await store.FindAsync(context.WorkflowExecutionId, completion.CompletedChildActivityExecutionId)
            ?? throw new InvalidOperationException("The exact completed body is absent.");
        if (body.ParentActivityExecutionId != context.ActivityExecutionState.InvocationId || body.Status != ActivityExecutionStatus.Completed)
            throw new InvalidOperationException("The body must be the current leaf's exact completed child.");
        var observer = (await store.ListAllAsync(context.WorkflowExecutionId)).Single(item =>
            item.ParentActivityExecutionId == body.InvocationId && item.Execution.ExecutableNodeId == ScopeProbeNames.Observer(stage));
        if (observer.Status != ActivityExecutionStatus.Completed)
            throw new InvalidOperationException("The exact body's observer is not complete.");
        var observed = observer.Completion?.Result.InlineValue?.Deserialize<ScopeObservation>(ProbeNames.Json)
            ?? throw new InvalidOperationException("Observer has no ordinary typed result.");
        return Finish(new(context.ActivityExecutionState.InvocationId, body.InvocationId, observer.InvocationId, observed));
    }
}

[Activity(Icon = "Eye")]
[ActivityOutcome(ActivityOutcomes.Done)]
public sealed class ScopeValueObserver : Activity<ScopeObservation>
{
    [ActivityInput] public int Stage { get; set; }
    [ActivityInput] public int Machine { get; set; }
    [ActivityInput] public int Parent { get; set; }
    [ActivityInput] public int Leaf { get; set; }

    protected override ValueTask<ActivityTransition<ScopeObservation>> ExecuteAsync(ActivityExecutionContext context) =>
        ValueTask.FromResult(ActivityTransition.Complete(new ScopeObservation(Stage, Machine, Parent, Leaf)));
}
