using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
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
            RuntimeInputBindingSource.Literal => new RuntimeResolvedInput(binding.InputName, binding.Source, binding.LiteralValue, null)
            {
                Envelope = binding.Literal
            },
            RuntimeInputBindingSource.Expression => new RuntimeResolvedInput(binding.InputName, binding.Source, null, binding.Expression),
            RuntimeInputBindingSource.WorkflowRequest => ResolveWorkflowRequest(binding, context),
            RuntimeInputBindingSource.VariableRead => ResolveVariable(binding, context),
            RuntimeInputBindingSource.ActivityResult => ResolveActivityResult(binding, context),
            _ => throw new ArgumentOutOfRangeException(nameof(binding), binding.Source, "Unsupported runtime input binding source.")
        };
    }

    private static RuntimeResolvedInput ResolveWorkflowRequest(RuntimeInputBinding binding, RuntimeInputBindingResolutionContext context)
    {
        var reference = binding.WorkflowRequest!;
        if (!context.WorkflowInputEnvelopes.TryGetValue(reference.MemberKey, out var envelope))
            throw new InvalidOperationException($"Workflow request member '{reference.MemberKey}' for input '{binding.InputName}' is unavailable.");
        return ResolveWorkflowRequestEnvelope(binding, reference, envelope);
    }

    private static RuntimeResolvedInput ResolveVariable(RuntimeInputBinding binding, RuntimeInputBindingResolutionContext context)
    {
        var reference = binding.Variable!;
        var address = new RuntimeVariableValueAddress(reference.DeclaringScopeId, reference.VariableKey);
        if (context.VariableEnvelopes.TryGetValue(address, out var envelope))
        {
            return new RuntimeResolvedInput(binding.InputName, binding.Source, envelope.InlineValue, null)
            {
                Envelope = Retype(envelope, binding.TargetType)
            };
        }

        throw new InvalidOperationException($"Variable '{reference.VariableKey}' in scope '{reference.DeclaringScopeId}' for input '{binding.InputName}' is unavailable.");
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
            return new RuntimeResolvedInput(binding.InputName, binding.Source, unavailable.InlineValue, null)
            {
                Envelope = unavailable
            };
        }

        var result = resolution.Completion.Result;
        if (StringComparer.Ordinal.Equals(reference.ProjectionKey, "$result"))
        {
            return new RuntimeResolvedInput(binding.InputName, binding.Source, result.InlineValue, null)
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
            return new RuntimeResolvedInput(binding.InputName, binding.Source, null, null)
            {
                Envelope = projectedNull
            };
        }
        if (!result.InlineValue.HasValue && result.ExternalReference is not null)
        {
            var externalProjectionSource = ValueEnvelope.External(binding.TargetType, result.ExternalReference, projectedPolicy);
            return new RuntimeResolvedInput(binding.InputName, binding.Source, null, null)
            {
                Envelope = externalProjectionSource
            };
        }
        if (!result.InlineValue.HasValue)
            throw new InvalidOperationException($"Activity result '{reference.ProjectionKey}' for input '{binding.InputName}' has no readable payload.");

        var value = result.InlineValue.Value;
        foreach (var segment in projection.Path.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (value.ValueKind != JsonValueKind.Object || !TryGetProperty(value, segment, out value))
                throw new InvalidOperationException($"Committed result from producer node '{reference.ProducerExecutableNodeId}' has no projection path '{projection.Path}'.");
        }

        var projectedEnvelope = value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
            ? ValueEnvelope.Null(binding.TargetType, projectedPolicy)
            : ValueEnvelope.Inline(binding.TargetType, value, projectedPolicy);
        return new RuntimeResolvedInput(binding.InputName, binding.Source, projectedEnvelope.InlineValue, null)
        {
            Envelope = projectedEnvelope
        };
    }

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
            return new RuntimeResolvedInput(binding.InputName, binding.Source, wholeEnvelope.InlineValue, null)
            {
                Envelope = wholeEnvelope
            };
        }

        if (!source.InlineValue.HasValue && source.ExternalReference is not null)
        {
            var externalProjectionSource = ValueEnvelope.External(binding.TargetType, source.ExternalReference, source.Policy);
            return new RuntimeResolvedInput(binding.InputName, binding.Source, null, null)
            {
                Envelope = externalProjectionSource
            };
        }
        if (!source.InlineValue.HasValue)
            throw new InvalidOperationException($"Workflow request path '{reference.Path}' for input '{binding.InputName}' cannot be projected from a null payload.");

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
        return new RuntimeResolvedInput(binding.InputName, binding.Source, envelope.InlineValue, null)
        {
            Envelope = envelope
        };
    }

    private static ValueEnvelope Retype(ValueEnvelope source, ValueTypeDescriptor targetType) =>
        new(targetType, source.Presence, source.InlineValue, source.ExternalReference, source.Policy);

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

}
