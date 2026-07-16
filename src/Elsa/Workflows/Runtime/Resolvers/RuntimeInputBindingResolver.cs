using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Expressions.Core.Models;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using System.Text.Json;

namespace Elsa.Workflows.Runtime.Core.Resolvers;

public sealed class RuntimeInputBindingResolver : IRuntimeInputBindingResolver
{
    public RuntimeResolvedInput Resolve(RuntimeInputBinding binding, RuntimeInputBindingResolutionContext context)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ArgumentNullException.ThrowIfNull(context);

        return binding.Source switch
        {
            RuntimeInputBindingSource.Literal => new RuntimeResolvedInput(binding.InputName, binding.Source, binding.LiteralValue, null, null, null)
            {
                Envelope = binding.Literal
            },
            RuntimeInputBindingSource.Expression => new RuntimeResolvedInput(binding.InputName, binding.Source, null, binding.Expression, null, null),
            RuntimeInputBindingSource.Reference => new RuntimeResolvedInput(binding.InputName, binding.Source, null, null, null, binding.Reference),
            RuntimeInputBindingSource.DurableValue => ResolveDurableValue(binding, context),
            RuntimeInputBindingSource.ActivityOutput => ResolveActivityOutput(binding, context),
            RuntimeInputBindingSource.WorkflowRequest => ResolveWorkflowRequest(binding, context),
            RuntimeInputBindingSource.VariableRead => ResolveVariable(binding, context),
            RuntimeInputBindingSource.ActivityResult => ResolveActivityResult(binding, context),
            _ => throw new ArgumentOutOfRangeException(nameof(binding), binding.Source, "Unsupported runtime input binding source.")
        };
    }

    private static RuntimeResolvedInput ResolveWorkflowRequest(RuntimeInputBinding binding, RuntimeInputBindingResolutionContext context)
    {
        var reference = binding.WorkflowRequest!;
        if (context.WorkflowInputEnvelopes.TryGetValue(reference.MemberKey, out var envelope))
            return ResolveWorkflowRequestEnvelope(binding, reference, envelope);

        if (!context.WorkflowInputs.TryGetValue(reference.MemberKey, out var value))
            throw new InvalidOperationException($"Workflow request member '{reference.MemberKey}' for input '{binding.InputName}' is unavailable.");

        var json = ToJson(value);
        if (!string.IsNullOrWhiteSpace(reference.Path))
        {
            var segments = reference.Path.Split('.', StringSplitOptions.RemoveEmptyEntries);
            var offset = segments.Length > 0 && StringComparer.OrdinalIgnoreCase.Equals(segments[0], reference.MemberKey) ? 1 : 0;
            for (var index = offset; index < segments.Length; index++)
            {
                if (json.ValueKind != JsonValueKind.Object || !TryGetProperty(json, segments[index], out json))
                    throw new InvalidOperationException($"Workflow request path '{reference.Path}' for input '{binding.InputName}' is unavailable.");
            }
        }

        return new RuntimeResolvedInput(binding.InputName, binding.Source, json, null, null, null);
    }

    private static RuntimeResolvedInput ResolveVariable(RuntimeInputBinding binding, RuntimeInputBindingResolutionContext context)
    {
        var reference = binding.Variable!;
        var address = new RuntimeVariableValueAddress(reference.DeclaringScopeId, reference.VariableKey);
        if (context.VariableEnvelopes.TryGetValue(address, out var envelope))
        {
            return new RuntimeResolvedInput(binding.InputName, binding.Source, envelope.InlineValue, null, null, null)
            {
                Envelope = Retype(envelope, binding.TargetType)
            };
        }

        if (context.VariableScope is null || !context.VariableScope.TryGetValue(
                new VariableReference(reference.VariableKey, reference.DeclaringScopeId),
                out var value))
            throw new InvalidOperationException($"Variable '{reference.VariableKey}' in scope '{reference.DeclaringScopeId}' for input '{binding.InputName}' is unavailable.");

        return new RuntimeResolvedInput(binding.InputName, binding.Source, ToJson(value), null, null, null);
    }

    private static RuntimeResolvedInput ResolveActivityResult(RuntimeInputBinding binding, RuntimeInputBindingResolutionContext context)
    {
        var reference = binding.ActivityResult!;
        var consumer = context.ConsumerInvocation
            ?? throw new InvalidOperationException($"Activity result input '{binding.InputName}' requires a consumer invocation identity.");
        var resolution = new CausalActivityResultResolver().Resolve(reference, consumer, context.RuntimeView);
        if (resolution is null)
        {
            var unavailable = ValueEnvelope.Null(binding.TargetType, binding.EffectivePolicy);
            return new RuntimeResolvedInput(binding.InputName, binding.Source, unavailable.InlineValue, null, null, null)
            {
                Envelope = unavailable
            };
        }

        var result = resolution.Completion.Result;
        if (StringComparer.Ordinal.Equals(reference.ProjectionKey, "$result"))
        {
            return new RuntimeResolvedInput(binding.InputName, binding.Source, result.InlineValue, null, null, null)
            {
                Envelope = Retype(result, binding.TargetType)
            };
        }

        var producerNode = context.Executable?.NodesById.GetValueOrDefault(reference.ProducerExecutableNodeId)
            ?? throw new InvalidOperationException($"Activity result input '{binding.InputName}' requires the pinned producer executable contract.");
        var projection = producerNode.ActivityContract?.Result.Projections.GetValueOrDefault(reference.ProjectionKey)
            ?? throw new InvalidOperationException($"Producer node '{reference.ProducerExecutableNodeId}' has no result projection '{reference.ProjectionKey}'.");
        var projectedPolicy = Combine(result.Policy, projection.Policy, reference.ProducerExecutableNodeId, projection.Key);

        if (result.Presence == ValuePresence.ExplicitNull)
        {
            var projectedNull = ValueEnvelope.Null(binding.TargetType, projectedPolicy);
            return new RuntimeResolvedInput(binding.InputName, binding.Source, null, null, null, null)
            {
                Envelope = projectedNull
            };
        }
        if (!result.InlineValue.HasValue)
            throw new InvalidOperationException($"Activity result '{reference.ProjectionKey}' for input '{binding.InputName}' uses external storage and cannot be projected without a payload reader.");

        var value = result.InlineValue.Value;
        foreach (var segment in projection.Path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (value.ValueKind != JsonValueKind.Object || !TryGetProperty(value, segment, out value))
                throw new InvalidOperationException($"Committed result from producer node '{reference.ProducerExecutableNodeId}' has no projection path '{projection.Path}'.");
        }

        var projectedEnvelope = value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
            ? ValueEnvelope.Null(binding.TargetType, projectedPolicy)
            : ValueEnvelope.Inline(binding.TargetType, value, projectedPolicy);
        return new RuntimeResolvedInput(binding.InputName, binding.Source, projectedEnvelope.InlineValue, null, null, null)
        {
            Envelope = projectedEnvelope
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

        if (!durableValue.InlineValue.HasValue && durableValue.ExternalReference is null)
            throw NewException(
                binding,
                RuntimeInputBindingResolutionFailureReason.DurableValueHasNoReadableValue,
                $"Durable value '{reference.ValueId}' for input '{binding.InputName}' has no inline value available to this resolver.",
                valueId: reference.ValueId);

        return new RuntimeResolvedInput(binding.InputName, binding.Source, durableValue.InlineValue, null, durableValue, null)
        {
            Envelope = ToEnvelope(durableValue, binding.TargetType)
        };
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

    private static JsonElement ToJson(object? value) => value is JsonElement json
        ? json.Clone()
        : JsonSerializer.SerializeToElement(value, value?.GetType() ?? typeof(object));

    private static bool TryGetProperty(JsonElement value, string name, out JsonElement property)
    {
        if (value.TryGetProperty(name, out property))
            return true;
        foreach (var candidate in value.EnumerateObject())
        {
            if (StringComparer.OrdinalIgnoreCase.Equals(candidate.Name, name))
            {
                property = candidate.Value;
                return true;
            }
        }

        property = default;
        return false;
    }

    private static RuntimeResolvedInput ResolveWorkflowRequestEnvelope(
        RuntimeInputBinding binding,
        RuntimeWorkflowRequestReference reference,
        ValueEnvelope source)
    {
        if (string.IsNullOrWhiteSpace(reference.Path))
        {
            var wholeEnvelope = Retype(source, binding.TargetType);
            return new RuntimeResolvedInput(binding.InputName, binding.Source, wholeEnvelope.InlineValue, null, null, null)
            {
                Envelope = wholeEnvelope
            };
        }

        if (!source.InlineValue.HasValue)
            throw new InvalidOperationException($"Workflow request path '{reference.Path}' for input '{binding.InputName}' cannot be projected from an external or null payload.");

        var json = source.InlineValue.Value;
        var segments = reference.Path.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var offset = segments.Length > 0 && StringComparer.OrdinalIgnoreCase.Equals(segments[0], reference.MemberKey) ? 1 : 0;
        for (var index = offset; index < segments.Length; index++)
        {
            if (json.ValueKind != JsonValueKind.Object || !TryGetProperty(json, segments[index], out json))
                throw new InvalidOperationException($"Workflow request path '{reference.Path}' for input '{binding.InputName}' is unavailable.");
        }

        var envelope = json.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
            ? ValueEnvelope.Null(binding.TargetType, source.Policy)
            : ValueEnvelope.Inline(binding.TargetType, json, source.Policy);
        return new RuntimeResolvedInput(binding.InputName, binding.Source, envelope.InlineValue, null, null, null)
        {
            Envelope = envelope
        };
    }

    private static ValueEnvelope Retype(ValueEnvelope source, ValueTypeDescriptor targetType) =>
        new(targetType, source.Presence, source.InlineValue, source.ExternalReference, source.Policy);

    private static ValueEnvelope ToEnvelope(DurableValueState value, ValueTypeDescriptor targetType)
    {
        var policy = new ValueProtectionPolicy(
            value.Lifecycle,
            value.Storage,
            IsTrue(value.Metadata, RuntimeMetadataKeys.IsSensitive),
            IsTrue(value.Metadata, RuntimeMetadataKeys.RequiresEncryption),
            value.Metadata.GetValueOrDefault(RuntimeMetadataKeys.RedactionMode),
            value.Metadata.GetValueOrDefault(RuntimeMetadataKeys.RetentionPolicy));

        if (value.ExternalReference is not null)
            return ValueEnvelope.External(targetType, value.ExternalReference, policy);
        if (value.InlineValue.HasValue && value.InlineValue.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return ValueEnvelope.Null(targetType, policy);
        if (value.InlineValue.HasValue)
            return ValueEnvelope.Inline(targetType, value.InlineValue.Value, policy);
        throw new InvalidOperationException($"Durable value '{value.ValueId}' has no materialized inline or external payload.");
    }

    private static ValueProtectionPolicy Combine(
        ValueProtectionPolicy source,
        ActivityValuePolicy projection,
        string producerNodeId,
        string projectionKey)
    {
        if (source.RedactionMode is not null && projection.RedactionMode is not null &&
            !StringComparer.Ordinal.Equals(source.RedactionMode, projection.RedactionMode))
        {
            throw new InvalidOperationException(
                $"Result projection '{projectionKey}' on producer node '{producerNodeId}' declares redaction mode " +
                $"'{projection.RedactionMode}', which is incompatible with source mode '{source.RedactionMode}'.");
        }

        return new ValueProtectionPolicy(
            source.Lifecycle,
            source.Storage,
            source.IsSensitive || projection.IsSensitive,
            source.RequiresEncryption || projection.RequiresEncryption,
            projection.RedactionMode ?? source.RedactionMode,
            source.RetentionPolicy,
            source.Metadata);
    }

    private static bool IsTrue(IReadOnlyDictionary<string, string> metadata, string key) =>
        metadata.TryGetValue(key, out var value) && bool.TryParse(value, out var parsed) && parsed;
}
