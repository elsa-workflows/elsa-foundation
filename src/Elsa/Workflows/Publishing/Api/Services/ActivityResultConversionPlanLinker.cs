using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Api.Services;

/// <summary>
/// Resolves the source contracts of direct activity-result bindings after the whole executable tree is
/// available. Authoring records only the structural producer reference; this linker pins the producer's
/// immutable result contract into the consumer's conversion plan before the executable is hashed.
/// </summary>
public sealed class ActivityResultConversionPlanLinker(ValueConversionPlanResolver conversionPlanResolver)
{
    public ExecutableNode Link(ExecutableNode root)
    {
        ArgumentNullException.ThrowIfNull(root);

        var nodesById = Flatten(root).ToDictionary(node => node.ExecutableNodeId, StringComparer.Ordinal);
        return LinkNode(root, nodesById);
    }

    private ExecutableNode LinkNode(ExecutableNode node, IReadOnlyDictionary<string, ExecutableNode> nodesById)
    {
        var inputBindings = node.InputBindings.ToDictionary(
            item => item.Key,
            item => LinkBinding(node, item.Value, nodesById),
            StringComparer.Ordinal);
        var childSlots = node.ChildSlots
            .Select(slot => new ExecutableChildSlot(
                slot.Name,
                slot.Activities.Select(child => LinkNode(child, nodesById)).ToArray(),
                slot.OperatorSchedulingCapability))
            .ToArray();

        return new ExecutableNode(
            node.ExecutableNodeId,
            node.AuthoredActivityId,
            node.ActivityType,
            node.ActivityTypeVersion,
            node.Descriptor,
            inputBindings,
            node.OutputCaptures,
            node.Metadata,
            childSlots,
            node.Structure,
            node.ActivityContract,
            node.IntrinsicKind,
            node.IntrinsicVariable);
    }

    private RuntimeInputBinding LinkBinding(
        ExecutableNode consumer,
        RuntimeInputBinding binding,
        IReadOnlyDictionary<string, ExecutableNode> nodesById)
    {
        if (binding.Source != RuntimeInputBindingSource.ActivityResult || binding.ConversionPlan is not null)
            return binding;

        var reference = binding.ActivityResult!;
        var bindingContext = new ValueConversionBindingContext(
            consumer.ExecutableNodeId,
            binding.InputName,
            ValueConversionBindingKind.ActivityResult);
        if (!nodesById.TryGetValue(reference.ProducerExecutableNodeId, out var producer))
        {
            throw ValueConversionPublicationException.SourceContractUnavailable(
                $"VF-COER-001: Activity result input '{binding.InputName}' on consumer node '{consumer.ExecutableNodeId}' " +
                $"references producer node '{reference.ProducerExecutableNodeId}', which is not present in the compiled executable.",
                ValueConversionRejectionReason.ProducerNodeMissing,
                binding.TargetType,
                binding.ConversionRequest?.Mode ?? ValueConversionMode.Auto,
                binding.ConversionRequest?.Profile,
                bindingContext);
        }

        var result = producer.ActivityContract?.Result
            ?? throw ValueConversionPublicationException.SourceContractUnavailable(
                $"VF-COER-001: Activity result input '{binding.InputName}' on consumer node '{consumer.ExecutableNodeId}' " +
                $"references producer node '{producer.ExecutableNodeId}', which has no pinned activity result contract.",
                ValueConversionRejectionReason.ProducerResultContractMissing,
                binding.TargetType,
                binding.ConversionRequest?.Mode ?? ValueConversionMode.Auto,
                binding.ConversionRequest?.Profile,
                bindingContext);
        var (sourceType, sourceRepresentation) = StringComparer.Ordinal.Equals(reference.ProjectionKey, "$result")
            ? (result.Type, result.EffectiveSourceRepresentation)
            : ResolveProjectionContract(binding, consumer, reference, producer, result, bindingContext);
        var plan = conversionPlanResolver.Resolve(
            sourceType,
            sourceRepresentation,
            binding.TargetType,
            binding.ConversionRequest?.Mode ?? ValueConversionMode.Auto,
            binding.ConversionRequest?.Profile,
            binding.ConversionRequest?.Limits,
            binding.ConversionRequest?.Options,
            bindingContext);

        return new RuntimeInputBinding(
            binding.InputName,
            binding.TargetType,
            binding.EffectivePolicy,
            binding.Source,
            binding.Literal,
            binding.WorkflowRequest,
            binding.Variable,
            binding.ActivityResult,
            binding.Expression,
            binding.Metadata,
            plan);
    }

    private static (ValueTypeDescriptor Type, ValueRepresentation Representation) ResolveProjectionContract(
        RuntimeInputBinding binding,
        ExecutableNode consumer,
        RuntimeActivityResultReference reference,
        ExecutableNode producer,
        ActivityResultContract result,
        ValueConversionBindingContext bindingContext)
    {
        if (!result.Projections.TryGetValue(reference.ProjectionKey, out var projection))
            throw ValueConversionPublicationException.SourceContractUnavailable(
                $"VF-COER-001: Activity result input '{binding.InputName}' on consumer node '{consumer.ExecutableNodeId}' " +
                $"references unknown result projection '{reference.ProjectionKey}' on producer node '{producer.ExecutableNodeId}'.",
                ValueConversionRejectionReason.UnknownResultProjection,
                binding.TargetType,
                binding.ConversionRequest?.Mode ?? ValueConversionMode.Auto,
                binding.ConversionRequest?.Profile,
                bindingContext);

        return (projection.Type, projection.EffectiveSourceRepresentation);
    }

    private static IEnumerable<ExecutableNode> Flatten(ExecutableNode root)
    {
        var pending = new Stack<ExecutableNode>();
        pending.Push(root);
        while (pending.TryPop(out var node))
        {
            yield return node;
            foreach (var child in node.ChildSlots.SelectMany(slot => slot.Activities).Reverse())
                pending.Push(child);
        }
    }
}
