using Elsa.Activities.Design.Core.Models;
using Elsa.Events.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Events;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.EFCore.Commands;
using Elsa.Workflows.Design.Persistence.EFCore.Contracts;

namespace Elsa.Workflows.Design.Persistence.EFCore.Services;

/// <summary>
/// Pure, internal diff engine for <see cref="UpdateDraft"/> (Unit 2). Given the stored
/// Draft state+layout and the complete desired state+layout, derives the ordered list of
/// per-concept mutation events — one event per detected difference — using the existing 20
/// <c>Elsa.Workflows.Design.Core.Events</c> mutation event types. The per-concept derivation
/// logic was migrated wholesale from the 20 deleted granular command bodies (research R3/R4).
/// </summary>
/// <remarks>
/// <para>
/// <b>Match keys (FR-023, research R2).</b> Variables/Inputs/Outputs by <c>ReferenceKey</c>;
/// activities by <c>NodeId</c>; per-activity I/O by (<c>NodeId</c>, <c>ReferenceKey</c>);
/// layout records by <c>NodeId</c>.
/// </para>
/// <para>
/// <b>Identity vs payload (FR-013d).</b> Same key + changed payload → a single UPDATE event;
/// a differing key → a REMOVE of the old + an ADD of the new. There is no activity-body update
/// event — a matched activity (same <c>NodeId</c>) only diffs its I/O; structural-body changes
/// persist via the wholesale state assignment but emit no granular event (parity with the
/// former command surface, which had no such command).
/// </para>
/// <para>
/// <b>Deterministic order.</b> Events are emitted grouped by dimension in this fixed order:
/// variables, workflow inputs, workflow outputs, activities (added then removed), per-activity
/// I/O, layout moves. Within a dimension, order follows the
/// desired/stored enumeration order.
/// </para>
/// <para>
/// Not a public contract (G2/G25) — <c>internal</c> collaborator of the command. Declared
/// <c>public sealed</c> per G27 for cross-assembly test visibility via the test project.
/// </para>
/// </remarks>
public sealed class DraftStateDiffEngine : IDraftStateDiffEngine
{
    public IReadOnlyList<IEvent> Evaluate(
        string draftId,
        WorkflowDefinitionState stored,
        IReadOnlyCollection<DesignMetadataRecord> storedLayout,
        WorkflowDefinitionState desired,
        IReadOnlyCollection<DesignMetadataRecord> desiredLayout
    )
    {
        var events = new List<IEvent>();

        DiffVariables(draftId, stored, desired, events);
        DiffWorkflowInputs(draftId, stored, desired, events);
        DiffWorkflowOutputs(draftId, stored, desired, events);
        DiffActivities(draftId, stored, desired, events);
        DiffLayout(draftId, storedLayout, desiredLayout, events);

        return events;
    }

    private static void DiffVariables(string draftId, WorkflowDefinitionState stored, WorkflowDefinitionState desired, List<IEvent> events)
    {
        var storedByKey = ByKey(stored.Variables, v => v.ReferenceKey);
        var desiredByKey = ByKey(desired.Variables, v => v.ReferenceKey);

        foreach (var variable in desired.Variables)
        {
            if (!storedByKey.TryGetValue(variable.ReferenceKey, out var old))
                events.Add(new OnVariableDeclaredInDraft(draftId, variable));
            else if (!Equals(old, variable))
                events.Add(new OnVariableUpdatedInDraft(draftId, variable.ReferenceKey, old, variable));
        }

        foreach (var variable in stored.Variables)
            if (!desiredByKey.ContainsKey(variable.ReferenceKey))
                events.Add(new OnVariableRemovedFromDraft(draftId, variable.ReferenceKey));
    }

    private static void DiffWorkflowInputs(string draftId, WorkflowDefinitionState stored, WorkflowDefinitionState desired, List<IEvent> events)
    {
        var storedByKey = ByKey(stored.Inputs, i => i.ReferenceKey);
        var desiredByKey = ByKey(desired.Inputs, i => i.ReferenceKey);

        foreach (var input in desired.Inputs)
        {
            if (!storedByKey.TryGetValue(input.ReferenceKey, out var old))
                events.Add(new OnWorkflowInputAddedToDraft(draftId, input));
            else if (!Equals(old, input))
                events.Add(new OnWorkflowInputUpdatedInDraft(draftId, input.ReferenceKey, old, input));
        }

        foreach (var input in stored.Inputs)
            if (!desiredByKey.ContainsKey(input.ReferenceKey))
                events.Add(new OnWorkflowInputRemovedFromDraft(draftId, input.ReferenceKey));
    }

    private static void DiffWorkflowOutputs(string draftId, WorkflowDefinitionState stored, WorkflowDefinitionState desired, List<IEvent> events)
    {
        var storedByKey = ByKey(stored.Outputs, o => o.ReferenceKey);
        var desiredByKey = ByKey(desired.Outputs, o => o.ReferenceKey);

        foreach (var output in desired.Outputs)
        {
            if (!storedByKey.TryGetValue(output.ReferenceKey, out var old))
                events.Add(new OnWorkflowOutputAddedToDraft(draftId, output));
            else if (!Equals(old, output))
                events.Add(new OnWorkflowOutputUpdatedInDraft(draftId, output.ReferenceKey, old, output));
        }

        foreach (var output in stored.Outputs)
            if (!desiredByKey.ContainsKey(output.ReferenceKey))
                events.Add(new OnWorkflowOutputRemovedFromDraft(draftId, output.ReferenceKey));
    }

    private static void DiffActivities(string draftId, WorkflowDefinitionState stored, WorkflowDefinitionState desired, List<IEvent> events)
    {
        var storedActivities = FlattenActivities(stored.RootActivity).ToArray();
        var desiredActivities = FlattenActivities(desired.RootActivity).ToArray();
        var storedByNode = ByKey(storedActivities, a => a.NodeId);
        var desiredByNode = ByKey(desiredActivities, a => a.NodeId);

        // Added (new NodeId) — the OnActivityAddedToDraft payload carries the whole node
        // (including its I/O), so no per-I/O events are derived for a brand-new activity.
        foreach (var activity in desiredActivities)
            if (!storedByNode.ContainsKey(activity.NodeId))
                events.Add(new OnActivityAddedToDraft(draftId, activity));

        // Removed (NodeId absent from desired) — its I/O leaves with it; only
        // OnActivityRemovedFromDraft is emitted for the node itself.
        foreach (var activity in storedActivities)
            if (!desiredByNode.ContainsKey(activity.NodeId))
                events.Add(new OnActivityRemovedFromDraft(draftId, activity.NodeId));

        // Matched (same NodeId) — diff per-activity I/O.
        foreach (var desiredActivity in desiredActivities)
        {
            if (!storedByNode.TryGetValue(desiredActivity.NodeId, out var storedActivity))
                continue;

            DiffActivityInputs(draftId, desiredActivity.NodeId, storedActivity, desiredActivity, events);
            DiffActivityOutputs(draftId, desiredActivity.NodeId, storedActivity, desiredActivity, events);
        }
    }

    private static void DiffActivityInputs(string draftId, string nodeId, ActivityNode stored, ActivityNode desired, List<IEvent> events)
    {
        var storedByKey = ByKey(stored.Inputs, i => i.ReferenceKey);
        var desiredByKey = ByKey(desired.Inputs, i => i.ReferenceKey);

        foreach (var input in desired.Inputs)
        {
            if (!storedByKey.TryGetValue(input.ReferenceKey, out var old))
                events.Add(new OnActivityInputAddedToDraft(draftId, nodeId, input));
            else if (!Equals(old, input))
                events.Add(new OnActivityInputUpdatedInDraft(draftId, nodeId, input.ReferenceKey, old, input));
        }

        foreach (var input in stored.Inputs)
            if (!desiredByKey.ContainsKey(input.ReferenceKey))
                events.Add(new OnActivityInputRemovedFromDraft(draftId, nodeId, input.ReferenceKey));
    }

    private static void DiffActivityOutputs(string draftId, string nodeId, ActivityNode stored, ActivityNode desired, List<IEvent> events)
    {
        var storedByKey = ByKey(stored.Outputs, o => o.ReferenceKey);
        var desiredByKey = ByKey(desired.Outputs, o => o.ReferenceKey);

        foreach (var output in desired.Outputs)
        {
            if (!storedByKey.TryGetValue(output.ReferenceKey, out var old))
                events.Add(new OnActivityOutputAddedToDraft(draftId, nodeId, output));
            else if (!Equals(old, output))
                events.Add(new OnActivityOutputUpdatedInDraft(draftId, nodeId, output.ReferenceKey, old, output));
        }

        foreach (var output in stored.Outputs)
            if (!desiredByKey.ContainsKey(output.ReferenceKey))
                events.Add(new OnActivityOutputRemovedFromDraft(draftId, nodeId, output.ReferenceKey));
    }

    private static void DiffLayout(string draftId, IReadOnlyCollection<DesignMetadataRecord> stored, IReadOnlyCollection<DesignMetadataRecord> desired, List<IEvent> events)
    {
        var storedByNode = ByKey(stored, r => r.NodeId);

        // A new or changed layout record for a node → a move. Removed layout records emit no
        // event (the activity removal — if any — already covers the node's disappearance; there
        // is no "layout removed" event).
        foreach (var record in desired)
        {
            if (storedByNode.TryGetValue(record.NodeId, out var old) && Equals(old, record))
                continue;

            events.Add(new OnActivityMovedInDraft(draftId, record.NodeId, record.X, record.Y, record.Width, record.Height));
        }
    }

    private static Dictionary<string, T> ByKey<T>(IEnumerable<T> items, Func<T, string> keySelector)
    {
        var map = new Dictionary<string, T>();
        foreach (var item in items)
            map[keySelector(item)] = item; // last-wins; domain guarantees unique keys
        return map;
    }

    private static IEnumerable<ActivityNode> FlattenActivities(ActivityNode? root)
    {
        if (root is null)
            yield break;

        var stack = new Stack<ActivityNode>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var node = stack.Pop();
            yield return node;

            foreach (var child in (node.ChildSlots ?? []).SelectMany(slot => slot.Activities))
                stack.Push(child);
        }
    }

}
