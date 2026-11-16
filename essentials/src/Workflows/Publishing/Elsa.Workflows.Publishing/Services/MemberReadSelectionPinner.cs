using System.Collections.Immutable;
using System.Text.Json;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Models;
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
    internal static IReadOnlyDictionary<string, ExecutableActivityTemplateMemberRead>? CaptureOccurrence(
        string occurrenceId, IEnumerable<ArgumentState> inputs, ImmutableArray<ActivityDataMemberReadFacts> facts)
    {
        var result = new Dictionary<string, ExecutableActivityTemplateMemberRead>(StringComparer.Ordinal);
        foreach (var input in inputs)
        {
            var candidates = facts.Where(fact => fact.IsKnown && fact.Path == $"{occurrenceId}/inputs/{input.ReferenceKey}").ToArray();
            if (candidates.Length == 0)
                continue;
            if (!StringComparer.OrdinalIgnoreCase.Equals(input.Value.ExpressionType, ActivityDataReadRoles.ActivityResultMember) ||
                input.Value.Value is not JsonElement { ValueKind: JsonValueKind.Object } payload ||
                !ActivityDataMemberPath.TryRead(payload, out var segments) ||
                !payload.TryGetProperty("producerNodeId", out var producer) || producer.ValueKind != JsonValueKind.String ||
                !payload.TryGetProperty("projectionKey", out var projection) || projection.ValueKind != JsonValueKind.String ||
                candidates is not [var fact] || fact.SelectedType is null || fact.ProfileRevision is null ||
                fact.ProducerNodeId != producer.GetString() || fact.ProjectionKey != projection.GetString() ||
                !fact.MemberPath.SequenceEqual(segments, StringComparer.Ordinal))
                throw Mismatch(occurrenceId, input.ReferenceKey);
            var optional = payload.TryGetProperty("isOptional", out var flag) && flag.ValueKind == JsonValueKind.True;
            result.Add(input.ReferenceKey, new(new RuntimeActivityResultReference(fact.ProducerNodeId, fact.ProjectionKey, null, optional),
                KnownSelection(fact, segments)));
        }
        return result.Count == 0 ? null : result;
    }

    // A dependency root's AuthoredActivityId belongs to its own template. Only the parent's occurrence id
    // identifies this consumer, and descendants belong to their own already-pinned placement frames.
    internal static ExecutableNode PinOccurrence(ExecutableNode node, string occurrenceId,
        IReadOnlyDictionary<string, ExecutableActivityTemplateMemberRead>? reads)
    {
        if (reads is null)
            return node;
        var bindings = node.InputBindings.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        foreach (var (key, read) in reads)
        {
            if (!bindings.TryGetValue(key, out var binding) || binding.Source != RuntimeInputBindingSource.ActivityResultMember ||
                binding.Selection is not { Mode: RuntimeReadSelectionMode.DeferredFit } selection || binding.ActivityResult is not { } source ||
                source.ProducerExecutableNodeId != read.Source.ProducerExecutableNodeId || source.ProjectionKey != read.Source.ProjectionKey ||
                source.ProducerScopeId != read.Source.ProducerScopeId || source.IsOptional != read.Source.IsOptional ||
                !selection.Segments.SequenceEqual(read.Selection.Segments, StringComparer.Ordinal))
                throw Mismatch(occurrenceId, key);
            bindings[key] = WithSelection(binding, read.Selection);
        }
        return Clone(node, bindings, node.ChildSlots);
    }

    private static ActivityDataSelectionRejectedException Mismatch(string occurrenceId, string key) =>
        new([new($"{occurrenceId}/inputs/{key}", "ActivityDataSelection/MemberContractMismatch",
            "The occurrence's pinned member contract does not match its exact authored result read.")]);

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
        return Clone(node, inputBindings, childSlots);
    }

    private static ExecutableNode Clone(ExecutableNode node, IReadOnlyDictionary<string, RuntimeInputBinding> inputBindings,
        IReadOnlyCollection<ExecutableChildSlot> childSlots) => new(
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

        return WithSelection(binding, KnownSelection(fact, selection.Segments));
    }

    private static RuntimeReadSelection KnownSelection(ActivityDataMemberReadFacts fact, IReadOnlyList<string> segments) =>
        new(RuntimeReadSelection.CurrentSchemaVersion, segments, RuntimeReadSelectionMode.Known,
            new ValueTypeDescriptor(fact.SelectedType!.Alias, fact.SelectedType.CollectionKind), fact.MayBeAbsent,
            fact.MayBeNull, fact.Representation, fact.ProfileRevision);

    private static RuntimeInputBinding WithSelection(RuntimeInputBinding binding, RuntimeReadSelection selection) => new(
            binding.InputName,
            binding.TargetType,
            binding.EffectivePolicy,
            binding.Source,
            selection,
            activityResult: binding.ActivityResult,
            metadata: binding.Metadata,
            conversionPlan: binding.ConversionPlan,
            conversionRequest: binding.ConversionRequest);
}
