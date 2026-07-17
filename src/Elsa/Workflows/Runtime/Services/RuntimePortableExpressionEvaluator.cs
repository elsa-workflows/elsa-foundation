using System.Text.Json;
using Elsa.Expressions.Core.Constants;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Resolvers;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// Evaluates canonical portable expressions from their closed runtime parameter snapshot.
/// Parameter lookup, external payload dereferencing, result projection, and dependency-policy
/// propagation are kept together so every runtime consumer observes the same durable boundary.
/// </summary>
internal sealed class RuntimePortableExpressionEvaluator(IExternalPayloadStore? externalPayloadStore)
{
    public async ValueTask<PortableExpressionEvaluation> EvaluateAsync(
        RuntimeExpressionBinding expression,
        ValueTypeDescriptor targetType,
        ValueProtectionPolicy ownerPolicy,
        string nodeId,
        string inputName,
        RuntimeInputBindingResolutionContext resolutionContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(expression);
        ArgumentNullException.ThrowIfNull(ownerPolicy);
        if (!StringComparer.Ordinal.Equals(expression.CapabilityProfile, ExpressionCapabilityProfiles.BindingPureV1))
            throw new InvalidOperationException($"Canonical expression input '{inputName}' on executable node '{nodeId}' requires capability profile '{ExpressionCapabilityProfiles.BindingPureV1}'.");

        var serviceProvider = resolutionContext.ServiceProvider
            ?? throw new InvalidOperationException($"Input '{inputName}' on executable node '{nodeId}' uses a portable '{expression.Language}' expression, but no service provider was supplied to evaluate it.");
        var portableEvaluator = serviceProvider.GetService(typeof(IPortableExpressionEvaluator)) as IPortableExpressionEvaluator
            ?? throw new InvalidOperationException($"Input '{inputName}' on executable node '{nodeId}' uses a portable '{expression.Language}' expression, but no '{nameof(IPortableExpressionEvaluator)}' is registered.");
        var definition = new ExpressionDefinition(
            expression.Language,
            expression.Expression,
            targetType.ToTypeReference(),
            expression.Parameters,
            expression.Options,
            expression.CapabilityProfile,
            expression.Metadata);
        var parameters = await MaterializeParametersAsync(expression, resolutionContext, nodeId, inputName, cancellationToken);
        var request = new ExpressionEvaluationRequest(definition, parameters.Values, cancellationToken);
        var effectivePolicy = parameters.DependencyPolicy is null
            ? ownerPolicy
            : ValuePolicyCombiner.Combine(
                ownerPolicy,
                parameters.DependencyPolicy,
                $"Portable expression input '{inputName}' on executable node '{nodeId}'");

        try
        {
            return new PortableExpressionEvaluation(
                await portableEvaluator.EvaluateAsync(request),
                effectivePolicy);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"Input '{inputName}' on executable node '{nodeId}' failed to evaluate its portable '{expression.Language}' expression with fingerprint '{definition.Fingerprint}'.",
                exception);
        }
    }

    private async ValueTask<PortableExpressionParameters> MaterializeParametersAsync(
        RuntimeExpressionBinding expression,
        RuntimeInputBindingResolutionContext context,
        string nodeId,
        string inputName,
        CancellationToken cancellationToken)
    {
        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        ValueProtectionPolicy? dependencyPolicy = null;
        foreach (var (name, binding) in expression.Parameters.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            var parameter = binding switch
            {
                LiteralExpressionParameterBinding literal => new PortableExpressionParameter(ReadLiteralParameter(literal, name, nodeId, inputName), null),
                WorkflowRequestExpressionParameterBinding request => await ReadWorkflowRequestParameterAsync(request, context, name, nodeId, inputName, cancellationToken),
                VariableExpressionParameterBinding variable => await ReadVariableParameterAsync(variable, context, name, nodeId, inputName, cancellationToken),
                ActivityResultExpressionParameterBinding result => await ReadActivityResultParameterAsync(result, context, name, nodeId, inputName, cancellationToken),
                _ => throw new InvalidOperationException($"Portable expression parameter '{name}' on input '{inputName}' uses unsupported binding type '{binding.GetType().Name}'.")
            };
            values.Add(name, parameter.Value);
            if (parameter.Policy is not null)
            {
                dependencyPolicy = dependencyPolicy is null
                    ? parameter.Policy
                    : ValuePolicyCombiner.Combine(
                        dependencyPolicy,
                        parameter.Policy,
                        $"Portable expression input '{inputName}' on executable node '{nodeId}'");
            }
        }

        return new PortableExpressionParameters(values, dependencyPolicy);
    }

    private static JsonElement ReadLiteralParameter(
        LiteralExpressionParameterBinding binding,
        string parameterName,
        string nodeId,
        string inputName)
    {
        if (binding.Value.ValueKind == JsonValueKind.Undefined)
            throw NewParameterException(parameterName, nodeId, inputName, "has an undefined literal");
        return binding.Value.Clone();
    }

    private async ValueTask<PortableExpressionParameter> ReadWorkflowRequestParameterAsync(
        WorkflowRequestExpressionParameterBinding binding,
        RuntimeInputBindingResolutionContext context,
        string parameterName,
        string nodeId,
        string inputName,
        CancellationToken cancellationToken)
    {
        if (!context.WorkflowInputEnvelopes.TryGetValue(binding.MemberKey, out var envelope))
            throw NewParameterException(parameterName, nodeId, inputName, $"references unavailable persistable workflow request member '{binding.MemberKey}'");

        var value = await ReadPersistableEnvelopeAsync(envelope, parameterName, nodeId, inputName, cancellationToken);
        return new PortableExpressionParameter(
            ProjectPath(value, binding.Path, binding.MemberKey, parameterName, nodeId, inputName),
            envelope.Policy);
    }

    private async ValueTask<PortableExpressionParameter> ReadVariableParameterAsync(
        VariableExpressionParameterBinding binding,
        RuntimeInputBindingResolutionContext context,
        string parameterName,
        string nodeId,
        string inputName,
        CancellationToken cancellationToken)
    {
        var address = new RuntimeVariableValueAddress(binding.DeclaringScopeNodeId, binding.VariableKey);
        if (!context.VariableEnvelopes.TryGetValue(address, out var envelope))
            throw NewParameterException(parameterName, nodeId, inputName, $"references unavailable variable '{binding.VariableKey}' in scope '{binding.DeclaringScopeNodeId}'");
        return new PortableExpressionParameter(
            await ReadPersistableEnvelopeAsync(envelope, parameterName, nodeId, inputName, cancellationToken),
            envelope.Policy);
    }

    private async ValueTask<PortableExpressionParameter> ReadActivityResultParameterAsync(
        ActivityResultExpressionParameterBinding binding,
        RuntimeInputBindingResolutionContext context,
        string parameterName,
        string nodeId,
        string inputName,
        CancellationToken cancellationToken)
    {
        var consumer = context.ConsumerInvocation
            ?? throw NewParameterException(parameterName, nodeId, inputName, "requires a consumer invocation identity");
        var resolution = new CausalActivityResultResolver().Resolve(binding, consumer, context.RuntimeView);
        if (resolution is null)
            return new PortableExpressionParameter(JsonSerializer.SerializeToElement<object?>(null), null);

        var result = resolution.Completion.Result;
        var value = await ReadPersistableEnvelopeAsync(result, parameterName, nodeId, inputName, cancellationToken);
        if (StringComparer.Ordinal.Equals(binding.ProjectionKey, "$result"))
            return new PortableExpressionParameter(value, result.Policy);

        var producerNode = context.Executable?.NodesById.GetValueOrDefault(binding.ProducerNodeId)
            ?? throw NewParameterException(parameterName, nodeId, inputName, $"requires the pinned contract for producer node '{binding.ProducerNodeId}'");
        var projection = producerNode.ActivityContract?.Result.Projections.GetValueOrDefault(binding.ProjectionKey)
            ?? throw NewParameterException(parameterName, nodeId, inputName, $"references unknown result projection '{binding.ProjectionKey}' on producer node '{binding.ProducerNodeId}'");
        return new PortableExpressionParameter(
            ProjectPath(value, projection.Path, null, parameterName, nodeId, inputName),
            result.Policy);
    }

    private async ValueTask<JsonElement> ReadPersistableEnvelopeAsync(
        ValueEnvelope envelope,
        string parameterName,
        string nodeId,
        string inputName,
        CancellationToken cancellationToken)
    {
        if (envelope.Policy.Lifecycle == DurableValueLifecycle.None)
            throw NewParameterException(parameterName, nodeId, inputName, "is transient and cannot cross the durable expression boundary");
        if (envelope.Presence == ValuePresence.Absent)
            throw NewParameterException(parameterName, nodeId, inputName, "is absent");
        if (envelope.Presence == ValuePresence.ExplicitNull)
            return JsonSerializer.SerializeToElement<object?>(null);
        if (envelope.InlineValue.HasValue)
            return envelope.InlineValue.Value.Clone();
        if (envelope.ExternalReference is null)
            throw NewParameterException(parameterName, nodeId, inputName, "has no materialized payload");
        if (externalPayloadStore is null)
            throw NewParameterException(parameterName, nodeId, inputName, "uses external storage without a payload reader");
        return await externalPayloadStore.ReadAsync(envelope.ExternalReference, cancellationToken);
    }

    private static JsonElement ProjectPath(
        JsonElement value,
        string? path,
        string? rootName,
        string parameterName,
        string nodeId,
        string inputName)
    {
        if (string.IsNullOrWhiteSpace(path))
            return value;

        var segments = path.Split('.', StringSplitOptions.RemoveEmptyEntries);
        var offset = segments.Length > 0 && rootName is not null && StringComparer.OrdinalIgnoreCase.Equals(segments[0], rootName) ? 1 : 0;
        for (var index = offset; index < segments.Length; index++)
        {
            if (value.ValueKind != JsonValueKind.Object || !TryGetProperty(value, segments[index], out value))
                throw NewParameterException(parameterName, nodeId, inputName, $"has unavailable path '{path}'");
        }

        return value.Clone();
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

    private static InvalidOperationException NewParameterException(
        string parameterName,
        string nodeId,
        string inputName,
        string reason) =>
        new($"Portable expression parameter '{parameterName}' for input '{inputName}' on executable node '{nodeId}' {reason}.");
}

internal sealed record PortableExpressionEvaluation(JsonElement Value, ValueProtectionPolicy EffectivePolicy);

internal sealed record PortableExpressionParameters(
    IReadOnlyDictionary<string, JsonElement> Values,
    ValueProtectionPolicy? DependencyPolicy);

internal sealed record PortableExpressionParameter(JsonElement Value, ValueProtectionPolicy? Policy);
