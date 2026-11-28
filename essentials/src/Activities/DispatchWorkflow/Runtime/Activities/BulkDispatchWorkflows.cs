using System.Globalization;
using System.Text.Json;
using Elsa.Activities.DispatchWorkflow.Runtime.Constants;
using Elsa.Activities.DispatchWorkflow.Runtime.Services;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.DispatchWorkflow.Runtime.Activities;

/// <summary>
/// Starts one child workflow per item (spec 1242). For each item of <see cref="Items"/> it runs the authored
/// <c>DispatchWorkflow</c> in its <c>Dispatch</c> slot once, under its own iteration id and with an iteration scope
/// holding <c>currentItem</c> and <c>currentIndex</c>. It routes each pass by the dispatch outcome: <c>Completed</c>
/// runs the <c>ChildCompleted</c> slot, <c>Faulted</c>, <c>Cancelled</c> and <c>DispatchFailed</c> run the
/// <c>ChildFaulted</c> slot, and <c>Dispatched</c> (the node did not wait) ends the pass. At most
/// <see cref="BulkDispatchWorkflowsConstants.Window"/> passes are open at once, and it completes with <c>Done</c> when
/// every pass has ended.
/// </summary>
/// <remarks>
/// <para>
/// Each pass's dispatch is an ordinary <c>DispatchWorkflow</c> activity execution, so dispatch identity, pinning,
/// resume, cancellation, audit and recovery apply unchanged (FR-004).
/// </para>
/// <para>
/// The activity implements no child-fault handler, as <c>ForEach</c> and <c>If</c> do not: the workflow's incident
/// strategy decides every fault of the <c>Dispatch</c> node or a slot (FR-008). Under ContinueWithIncidents the incident
/// stays open and the faulted pass keeps its place in the window. The activity keeps waiting for that child without
/// consuming its fault, as <c>Sequence</c> does, so an operator's <c>RescheduleActivity</c> of the faulted child reaches
/// it and the successor's completion ends the pass (<see cref="IRuntimeAcceptsRescheduledFaultedChild"/>).
/// </para>
/// </remarks>
[RefusesSecretBinding(nameof(Items), SecretBindingRefusalReason.PersistedByActivity)]
[Activity(Icon = "Send")]
[ActivityStructure(BulkDispatchWorkflowsConstants.StructureKind, BulkDispatchWorkflowsConstants.StructureSchemaVersion)]
[ActivityChildSlot(BulkDispatchWorkflowsConstants.DispatchSlotName, BulkDispatchWorkflowsConstants.DispatchSlotProperty,
    BulkDispatchWorkflowsConstants.DispatchSlotDisplayName, ActivityChildSlotCardinalities.Single)]
[ActivityChildSlot(BulkDispatchWorkflowsConstants.ChildCompletedSlotName, BulkDispatchWorkflowsConstants.ChildCompletedSlotProperty,
    BulkDispatchWorkflowsConstants.ChildCompletedSlotDisplayName, ActivityChildSlotCardinalities.Single)]
[ActivityChildSlot(BulkDispatchWorkflowsConstants.ChildFaultedSlotName, BulkDispatchWorkflowsConstants.ChildFaultedSlotProperty,
    BulkDispatchWorkflowsConstants.ChildFaultedSlotDisplayName, ActivityChildSlotCardinalities.Single)]
[ActivitySideEffectProfile(SideEffectProfile.ReplaySafe)]
public sealed class BulkDispatchWorkflows : StructuralActivity, IRuntimeStructuralActivity, IRuntimeActivityChildCompletionHandler,
    IRuntimeAcceptsRescheduledFaultedChild
{
    public const string ActivityType = BulkDispatchWorkflowsConstants.ActivityType;

    /// <summary>The items; one child workflow is dispatched per item. Evaluated once, then re-read per pass.</summary>
    [ActivityInput(Key = nameof(Items))]
    public object? Items { get; set; }

    public ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext runtimeContext)
    {
        ArgumentNullException.ThrowIfNull(runtimeContext);
        var navigator = runtimeContext.ExecutableNode.GetOrAddRoutingStructure(BulkDispatchWorkflowsNavigator.From);
        var items = BulkDispatchItems.Resolve(Items);

        if (items.Count > BulkDispatchWorkflowsConstants.ItemCap)
            return ValueTask.FromResult(RuntimeStructuralContinuation.Faulted(new ActivityFault(
                BulkDispatchWorkflowsConstants.CapExceededFaultCode,
                $"BulkDispatchWorkflows received {items.Count.ToString(CultureInfo.InvariantCulture)} items, more than the cap of " +
                $"{BulkDispatchWorkflowsConstants.ItemCap.ToString(CultureInfo.InvariantCulture)}. Split the items into batches and run one bulk dispatch per batch, for example from a sequential ForEach.")));

        if (items.Count == 0)
            return ValueTask.FromResult(RuntimeStructuralContinuation.Complete());

        if (navigator.Dispatch is not { } dispatch)
            throw new InvalidOperationException(
                $"BulkDispatchWorkflows executable node '{runtimeContext.ExecutableNode.ExecutableNodeId}' has no Dispatch node; publish refuses this (BulkDispatch/DispatchSlot).");

        var opened = Math.Min(items.Count, BulkDispatchWorkflowsConstants.Window);
        for (var index = 0; index < opened; index++)
            Schedule(runtimeContext, dispatch, items, index, "bulk-dispatch.dispatch");

        var state = new BulkDispatchWorkflowsState(items.Count, opened, [], []);
        return ValueTask.FromResult(state.Stage(RuntimeStructuralContinuation.Defer));
    }

    public ValueTask<RuntimeStructuralContinuation> OnChildCompletedAsync(ActivityChildCompletedContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        var runtimeContext = context.ParentContext as IRuntimeActivityExecutionContext
                             ?? throw new InvalidOperationException("BulkDispatchWorkflows requires an Elsa runtime activity execution context.");
        var navigator = runtimeContext.ExecutableNode.GetOrAddRoutingStructure(BulkDispatchWorkflowsNavigator.From);
        var state = BulkDispatchWorkflowsState.Load(runtimeContext.ActivityExecutionState);
        var index = ResolveIndex(runtimeContext, context.CompletedChildIterationId);

        // A repeated callback for a pass that already ended changes nothing, so it cannot open a further pass.
        if (state.IsEnded(index))
            return ValueTask.FromResult(state.AllEnded ? RuntimeStructuralContinuation.Complete() : RuntimeStructuralContinuation.Defer);

        if (navigator.IsDispatch(context.CompletedChildExecutableNodeId))
        {
            // The pass's slot already runs: a second Dispatch completion (a rescheduled Dispatch run) changes nothing.
            if (state.IsRouted(index))
                return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);

            if (SelectSlot(navigator, context.OutcomeNames) is { } slot)
            {
                Schedule(runtimeContext, slot, BulkDispatchItems.Resolve(Items), index, "bulk-dispatch.slot");
                return ValueTask.FromResult(state.Route(index).Stage(RuntimeStructuralContinuation.Defer));
            }
        }
        else if (!navigator.IsSlot(context.CompletedChildExecutableNodeId))
        {
            throw new InvalidOperationException(
                $"Completed child executable node '{context.CompletedChildExecutableNodeId}' is not a BulkDispatchWorkflows child.");
        }

        return ValueTask.FromResult(EndPass(runtimeContext, navigator, state, index));
    }

    // FR-006: Completed runs ChildCompleted; Faulted, Cancelled and DispatchFailed run ChildFaulted; Dispatched (the node
    // did not wait) runs nothing. A missing slot ends the pass.
    internal static ExecutableNode? SelectSlot(BulkDispatchWorkflowsNavigator navigator, IReadOnlyCollection<string> outcomes)
    {
        if (outcomes.Contains(DispatchWorkflowOutcomes.Completed, StringComparer.Ordinal))
            return navigator.ChildCompleted;
        if (outcomes.Contains(DispatchWorkflowOutcomes.Faulted, StringComparer.Ordinal) ||
            outcomes.Contains(DispatchWorkflowOutcomes.Cancelled, StringComparer.Ordinal) ||
            outcomes.Contains(DispatchWorkflowOutcomes.DispatchFailed, StringComparer.Ordinal))
            return navigator.ChildFaulted;
        return null;
    }

    private RuntimeStructuralContinuation EndPass(
        IRuntimeActivityExecutionContext runtimeContext,
        BulkDispatchWorkflowsNavigator navigator,
        BulkDispatchWorkflowsState state,
        int index)
    {
        state = state.End(index);
        if (state.AllEnded)
            return RuntimeStructuralContinuation.Complete();

        // Only a newly ended pass refills the window, one item at a time and in item order.
        if (state.Next < state.Count && navigator.Dispatch is { } dispatch)
        {
            Schedule(runtimeContext, dispatch, BulkDispatchItems.Resolve(Items), state.Next, "bulk-dispatch.dispatch");
            state = state with { Next = state.Next + 1 };
        }

        return state.Stage(RuntimeStructuralContinuation.Defer);
    }

    // Schedules one node of a pass under the pass's iteration id, with its own iteration frame. A frame belongs to one
    // activity execution, not to an iteration id, so a slot gets a fresh frame rebuilt from Items at the pass's index.
    private void Schedule(
        IRuntimeActivityExecutionContext runtimeContext,
        ExecutableNode target,
        BulkDispatchItems items,
        int index,
        string cause)
    {
        if (index >= items.Count)
            throw new InvalidOperationException(
                $"BulkDispatchWorkflows cannot schedule item {index.ToString(CultureInfo.InvariantCulture)}: Items now holds {items.Count.ToString(CultureInfo.InvariantCulture)} entries.");

        var ownerNodeId = runtimeContext.ExecutableNode.ExecutableNodeId;
        var parentActivityExecutionId = runtimeContext.ActivityExecutionState.Execution.ActivityExecutionId;
        var iterationId = BulkDispatchIteration.Id(parentActivityExecutionId, index);
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["bulkDispatch.parentActivityExecutionId"] = parentActivityExecutionId,
            ["bulkDispatch.targetNodeId"] = target.ExecutableNodeId,
            ["bulkDispatch.itemIndex"] = index.ToString(CultureInfo.InvariantCulture)
        };
        var values = new Dictionary<string, ValueEnvelope>(StringComparer.Ordinal)
        {
            [BulkDispatchWorkflowsConstants.CurrentItemVariableName] = ToItemEnvelope(runtimeContext, items.ItemAt(index)),
            [BulkDispatchWorkflowsConstants.CurrentIndexVariableName] = ToEnvelope(index, new ValueTypeDescriptor("Int32"), ValueProtectionPolicy.InstanceInline)
        };

        runtimeContext.ScheduleChildActivity(
            target.ExecutableNodeId,
            parentActivityExecutionId,
            metadata,
            ActivitySchedulingProvenance.From(
                runtimeContext.WorkflowExecutionId,
                parentActivityExecutionId: parentActivityExecutionId,
                schedulingActivityExecutionId: parentActivityExecutionId,
                branchId: null,
                iterationId: iterationId,
                executionPathId: null,
                executionScopeId: null,
                schedulingCause: cause,
                metadata: metadata),
            new LoopIterationScopeRequest(ownerNodeId, iterationId, values));
    }

    // The item keeps the durable policy of the Items binding, as ForEach's item does: the frame is persisted, so a
    // transient or externally stored collection is refused.
    private static ValueEnvelope ToItemEnvelope(IRuntimeActivityExecutionContext runtimeContext, object? value)
    {
        if (!runtimeContext.ExecutableNode.InputBindings.TryGetValue(nameof(Items), out var itemsBinding))
            return ToEnvelope(value, new ValueTypeDescriptor(TypeAliasConvention.AnyAlias), ValueProtectionPolicy.InstanceInline);

        var policy = itemsBinding.EffectivePolicy;
        if (policy.Lifecycle == DurableValueLifecycle.None)
            throw new InvalidOperationException("BulkDispatchWorkflows cannot create a durable iteration frame from a transient Items input.");
        if (policy.Storage is DurableValueStorage.External or DurableValueStorage.Custom)
            throw new InvalidOperationException($"BulkDispatchWorkflows cannot derive an item reference from Items storage strategy '{policy.Storage}'. Materialize the items through an explicit persistable projection first.");

        var itemsType = itemsBinding.TargetType;
        var itemType = new ValueTypeDescriptor(itemsType.Alias, CollectionKind.Single, itemsType.Schema, itemsType.SchemaVersion);
        return ToEnvelope(value, itemType, policy);
    }

    private static ValueEnvelope ToEnvelope(object? value, ValueTypeDescriptor type, ValueProtectionPolicy policy)
    {
        if (value is null)
            return ValueEnvelope.Null(type, policy);
        var json = value is JsonElement element
            ? element.Clone()
            : Elsa.Serialization.Core.JsonConversionContract.SerializeToElement(value);
        return ValueEnvelope.Inline(type, json, policy);
    }

    private static int ResolveIndex(IRuntimeActivityExecutionContext runtimeContext, string? iterationId)
    {
        var parentActivityExecutionId = runtimeContext.ActivityExecutionState.Execution.ActivityExecutionId;
        if (!BulkDispatchIteration.TryReadIndex(parentActivityExecutionId, iterationId, out var index))
            throw new InvalidOperationException(
                $"BulkDispatchWorkflows cannot route child iteration '{iterationId}': it is not an item pass of '{parentActivityExecutionId}'.");
        return index;
    }
}
