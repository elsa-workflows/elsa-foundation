using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Resolvers;

public sealed class RuntimeInputBindingResolver : IRuntimeInputBindingResolver
{
    public RuntimeResolvedInput Resolve(RuntimeInputBinding binding, RuntimeInputBindingResolutionContext context)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(context);

        return binding.Source switch
        {
            RuntimeInputBindingSource.Literal => new RuntimeResolvedInput(binding.InputName, binding.Source, binding.LiteralValue, null, null, null),
            RuntimeInputBindingSource.Expression => new RuntimeResolvedInput(binding.InputName, binding.Source, null, binding.Expression, null, null),
            RuntimeInputBindingSource.Reference => new RuntimeResolvedInput(binding.InputName, binding.Source, null, null, null, binding.Reference),
            RuntimeInputBindingSource.DurableValue => ResolveDurableValue(binding, context),
            RuntimeInputBindingSource.ActivityOutput => ResolveActivityOutput(binding, context),
            _ => throw new ArgumentOutOfRangeException(nameof(binding), binding.Source, "Unsupported runtime input binding source.")
        };
    }

    private static RuntimeResolvedInput ResolveDurableValue(RuntimeInputBinding binding, RuntimeInputBindingResolutionContext context)
    {
        var reference = binding.DurableValue!;

        if (!context.DurableValuesByValueId.TryGetValue(reference.ValueId, out var durableValue))
            throw NewException(
                binding,
                RuntimeInputBindingResolutionFailureReason.DurableValueMissing,
                $"Durable value '{reference.ValueId}' for input '{binding.InputName}' was not found.",
                valueId: reference.ValueId);

        if (!durableValue.InlineValue.HasValue)
            throw NewException(
                binding,
                RuntimeInputBindingResolutionFailureReason.DurableValueHasNoReadableValue,
                $"Durable value '{reference.ValueId}' for input '{binding.InputName}' has no inline value available to this resolver.",
                valueId: reference.ValueId);

        return new RuntimeResolvedInput(binding.InputName, binding.Source, durableValue.InlineValue, null, durableValue, null);
    }

    private static RuntimeResolvedInput ResolveActivityOutput(RuntimeInputBinding binding, RuntimeInputBindingResolutionContext context)
    {
        var reference = binding.ActivityOutput!;

        if (string.IsNullOrWhiteSpace(reference.ProducerActivityExecutionId))
            throw NewException(
                binding,
                RuntimeInputBindingResolutionFailureReason.AmbiguousActivityOutput,
                $"Activity output binding for input '{binding.InputName}' must name a concrete producer ActivityExecutionId.",
                outputName: reference.OutputName);

        var key = new ActiveActivityOutputKey(context.WorkflowExecutionId, reference.ProducerActivityExecutionId, reference.OutputName);
        if (!context.ActivityOutputs.TryGet(key, out var output))
            throw NewException(
                binding,
                RuntimeInputBindingResolutionFailureReason.ActivityOutputMissing,
                $"Active activity output '{reference.OutputName}' from activity execution '{reference.ProducerActivityExecutionId}' was not found for input '{binding.InputName}'.",
                activityExecutionId: reference.ProducerActivityExecutionId,
                outputName: reference.OutputName);

        return new RuntimeResolvedInput(binding.InputName, binding.Source, output.Value, null, null, null);
    }

    private static RuntimeInputBindingResolutionException NewException(
        RuntimeInputBinding binding,
        RuntimeInputBindingResolutionFailureReason reason,
        string message,
        string? activityExecutionId = null,
        string? outputName = null,
        string? valueId = null) =>
        new(message, reason, binding.InputName, activityExecutionId, outputName, valueId);
}
