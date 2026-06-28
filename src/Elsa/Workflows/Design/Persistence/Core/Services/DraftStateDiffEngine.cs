using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Events.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Events;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using ArgumentState = Elsa.Workflows.Design.Core.Models.ArgumentState;

namespace Elsa.Workflows.Design.Persistence.Core.Services;

public sealed class DraftStateDiffEngine(IActivityStructureService activityStructureService) : IDraftStateDiffEngine
{
    public IReadOnlyList<IEvent> Evaluate(
        string draftId,
        WorkflowDefinitionState stored,
        IReadOnlyCollection<DesignMetadataRecord> storedLayout,
        WorkflowDefinitionState desired,
        IReadOnlyCollection<DesignMetadataRecord> desiredLayout)
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

    private void DiffActivities(string draftId, WorkflowDefinitionState stored, WorkflowDefinitionState desired, List<IEvent> events)
    {
        var storedActivities = FlattenActivities(stored.RootActivity).ToArray();
        var desiredActivities = FlattenActivities(desired.RootActivity).ToArray();
        var storedByNode = ByKey(storedActivities, a => a.NodeId);
        var desiredByNode = ByKey(desiredActivities, a => a.NodeId);

        foreach (var activity in desiredActivities)
            if (!storedByNode.ContainsKey(activity.NodeId))
                events.Add(new OnActivityAddedToDraft(draftId, activity));

        foreach (var activity in storedActivities)
            if (!desiredByNode.ContainsKey(activity.NodeId))
                events.Add(new OnActivityRemovedFromDraft(draftId, activity.NodeId));

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
            else if (!ArgumentStatesEqual(old, input))
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
            else if (!ArgumentStatesEqual(old, output))
                events.Add(new OnActivityOutputUpdatedInDraft(draftId, nodeId, output.ReferenceKey, old, output));
        }

        foreach (var output in stored.Outputs)
            if (!desiredByKey.ContainsKey(output.ReferenceKey))
                events.Add(new OnActivityOutputRemovedFromDraft(draftId, nodeId, output.ReferenceKey));
    }

    private static void DiffLayout(string draftId, IReadOnlyCollection<DesignMetadataRecord> stored, IReadOnlyCollection<DesignMetadataRecord> desired, List<IEvent> events)
    {
        var storedByNode = ByKey(stored, r => r.NodeId);

        foreach (var record in desired)
        {
            if (storedByNode.TryGetValue(record.NodeId, out var old) && Equals(old, record))
                continue;

            events.Add(new OnActivityMovedInDraft(draftId, record.NodeId, record.X, record.Y, record.Width, record.Height));
        }
    }

    /// <summary>
    /// Structural equality for a matched (same <c>ReferenceKey</c>) activity argument across a
    /// persistence round-trip. The stored side is rehydrated from JSON, so its
    /// <see cref="ArgumentValue.Value"/> (typed <c>object?</c>) comes back as a
    /// <see cref="JsonElement"/> while the desired side carries the raw CLR value — record equality
    /// would report those as different even when semantically identical and emit a phantom "updated"
    /// event. Comparing the canonical JSON projection of each argument normalises both sides.
    /// </summary>
    private static bool ArgumentStatesEqual(ArgumentState stored, ArgumentState desired) =>
        stored.AutoEvaluate == desired.AutoEvaluate &&
        Equals(stored.EvaluatorType, desired.EvaluatorType) &&
        Equals(stored.StorageDriverType, desired.StorageDriverType) &&
        stored.IsSensitive == desired.IsSensitive &&
        StringComparer.Ordinal.Equals(stored.Value.ExpressionType, desired.Value.ExpressionType) &&
        StringComparer.Ordinal.Equals(CanonicalJson(stored.Value.Value), CanonicalJson(desired.Value.Value));

    private static string CanonicalJson(object? value) =>
        JsonSerializer.Serialize(value);

    private static Dictionary<string, T> ByKey<T>(IEnumerable<T> items, Func<T, string> keySelector)
    {
        var map = new Dictionary<string, T>();
        foreach (var item in items)
            map[keySelector(item)] = item;
        return map;
    }

    private IEnumerable<ActivityNode> FlattenActivities(ActivityNode? root)
    {
        if (root is null)
            yield break;

        var stack = new Stack<ActivityNode>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            var node = stack.Pop();
            yield return node;

            foreach (var child in activityStructureService.ProjectChildren(node).SelectMany(slot => slot.Activities))
                stack.Push(child);
        }
    }
}
