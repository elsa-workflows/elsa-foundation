using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Services;

/// <summary>
/// Pins, into each node of a compiled tree, the value adapter version of every expression language the node uses (spec
/// 1228 FR-028). It runs on the complete tree, placed reusable activities included, before the executable is hashed, so
/// the pin is part of the artifact's identity.
/// </summary>
internal static class ValueAdapterPinner
{
    public static ExecutableNode Pin(
        ExecutableNode root,
        IReadOnlyCollection<ExpressionValueAdapter> adapters,
        IReadOnlyCollection<ScriptActivityLanguage> scriptActivities)
    {
        ArgumentNullException.ThrowIfNull(root);
        return adapters.Count == 0 ? root : PinNode(root, adapters, scriptActivities);
    }

    private static ExecutableNode PinNode(
        ExecutableNode node,
        IReadOnlyCollection<ExpressionValueAdapter> adapters,
        IReadOnlyCollection<ScriptActivityLanguage> scriptActivities)
    {
        var changed = false;
        var childSlots = node.ChildSlots.Select(slot =>
        {
            var children = slot.Activities.Select(child => PinNode(child, adapters, scriptActivities)).ToArray();
            if (children.Zip(slot.Activities).All(pair => ReferenceEquals(pair.First, pair.Second)))
                return slot;
            changed = true;
            return new ExecutableChildSlot(slot.Name, children, slot.OperatorSchedulingCapability);
        }).ToArray();

        var required = ExecutableNodeValueAdapters.Required(node.ActivityType, node.InputBindings.Values, adapters, scriptActivities);
        var metadata = ExecutableNodeValueAdapters.Pin(node.Metadata, required);
        changed |= !ReferenceEquals(metadata, node.Metadata);
        if (!changed)
            return node;

        return new ExecutableNode(
            executableNodeId: node.ExecutableNodeId,
            authoredActivityId: node.AuthoredActivityId,
            activityType: node.ActivityType,
            activityTypeVersion: node.ActivityTypeVersion,
            descriptor: node.Descriptor,
            inputBindings: node.InputBindings,
            outputCaptures: node.OutputCaptures,
            metadata: metadata,
            executionPlacement: node.ExecutionPlacement,
            childSlots: childSlots,
            structure: node.Structure,
            activityContract: node.ActivityContract,
            intrinsicKind: node.IntrinsicKind,
            intrinsicVariable: node.IntrinsicVariable);
    }
}
