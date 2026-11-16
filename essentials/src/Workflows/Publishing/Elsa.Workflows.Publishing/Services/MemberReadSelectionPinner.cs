using System.Collections.Immutable;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Validations.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Services;

/// <summary>
/// Pins the selected member's contract into each member read the data selection analysis typed. The binding compiler
/// emits every member read as deferred, because it runs before the analysis; this runs right after it, so a read the
/// analysis could not type, or one it did not see, stays deferred and is checked by the runtime when it reads.
/// </summary>
internal static class MemberReadSelectionPinner
{
    public static ExecutableNode Pin(ExecutableNode root, ImmutableArray<ActivityDataMemberReadFacts> facts)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (facts.IsDefaultOrEmpty)
            return root;
        var known = facts.Where(fact => fact.IsKnown)
            .GroupBy(fact => fact.Path, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);
        return known.Count == 0 ? root : PinNode(root, known);
    }

    private static ExecutableNode PinNode(ExecutableNode node, IReadOnlyDictionary<string, ActivityDataMemberReadFacts[]> known)
    {
        var changed = false;
        var inputBindings = new Dictionary<string, RuntimeInputBinding>(StringComparer.Ordinal);
        foreach (var (key, binding) in node.InputBindings)
        {
            var pinned = PinBinding(node, binding, known);
            changed |= !ReferenceEquals(pinned, binding);
            inputBindings[key] = pinned;
        }

        var childSlots = node.ChildSlots.Select(slot =>
        {
            var children = slot.Activities.Select(child => PinNode(child, known)).ToArray();
            if (children.Zip(slot.Activities).All(pair => ReferenceEquals(pair.First, pair.Second)))
                return slot;
            changed = true;
            return new ExecutableChildSlot(slot.Name, children, slot.OperatorSchedulingCapability);
        }).ToArray();

        if (!changed)
            return node;
        return new ExecutableNode(
            executableNodeId: node.ExecutableNodeId,
            authoredActivityId: node.AuthoredActivityId,
            activityType: node.ActivityType,
            activityTypeVersion: node.ActivityTypeVersion,
            descriptor: node.Descriptor,
            inputBindings: inputBindings,
            outputCaptures: node.OutputCaptures,
            metadata: node.Metadata,
            executionPlacement: node.ExecutionPlacement,
            childSlots: childSlots,
            structure: node.Structure,
            activityContract: node.ActivityContract,
            intrinsicKind: node.IntrinsicKind,
            intrinsicVariable: node.IntrinsicVariable);
    }

    private static RuntimeInputBinding PinBinding(ExecutableNode node, RuntimeInputBinding binding,
        IReadOnlyDictionary<string, ActivityDataMemberReadFacts[]> known)
    {
        if (binding.Source != RuntimeInputBindingSource.ActivityResultMember ||
            binding.Selection is not { Mode: RuntimeReadSelectionMode.DeferredFit } selection ||
            binding.ActivityResult is not { } result ||
            !known.TryGetValue($"{node.AuthoredActivityId}/inputs/{binding.InputKey}", out var candidates))
            return binding;

        // The facts must describe this exact read: the same producer, projection and segments. Anything but exactly
        // one such fact leaves the read deferred, so the runtime checks it, rather than guessing between facts.
        var matches = candidates.Where(candidate =>
            StringComparer.Ordinal.Equals(candidate.ProducerNodeId, result.ProducerExecutableNodeId) &&
            StringComparer.Ordinal.Equals(candidate.ProjectionKey, result.ProjectionKey) &&
            candidate.MemberPath.SequenceEqual(selection.Segments, StringComparer.Ordinal)).Take(2).ToArray();
        if (matches is not [{ SelectedType: { } selectedType, ProfileRevision: { } profileRevision } fact])
            return binding;

        return new RuntimeInputBinding(
            binding.InputName,
            binding.TargetType,
            binding.EffectivePolicy,
            binding.Source,
            new RuntimeReadSelection(
                selection.SchemaVersion,
                selection.Segments,
                RuntimeReadSelectionMode.Known,
                new ValueTypeDescriptor(selectedType.Alias, selectedType.CollectionKind),
                fact.MayBeAbsent,
                fact.MayBeNull,
                fact.Representation,
                profileRevision),
            activityResult: result,
            metadata: binding.Metadata,
            conversionPlan: binding.ConversionPlan,
            conversionRequest: binding.ConversionRequest);
    }
}
