using System.Text.Json;
using Elsa.Expressions.Core.Constants;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Services.Values;

/// <summary>
/// Evaluates canonical portable expressions from their closed runtime parameter snapshot.
/// Parameter lookup, external payload dereferencing, result projection, and dependency-policy
/// propagation are kept together so every runtime consumer observes the same durable boundary.
/// </summary>
internal sealed class RuntimePortableExpressionEvaluator(
    IPortableExpressionEvaluator? portableExpressionEvaluator,
    IExternalPayloadStore? externalPayloadStore,
    IRuntimeValueConversionExecutor? valueConversionExecutor = null)
{
    private readonly IRuntimeValueConversionExecutor _valueConversionExecutor = valueConversionExecutor ?? new RuntimeValueConversionExecutor();

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

        var portableEvaluator = portableExpressionEvaluator
            ?? throw new InvalidOperationException($"Input '{inputName}' on executable node '{nodeId}' uses a portable '{expression.Language}' expression, but no '{nameof(IPortableExpressionEvaluator)}' is registered.");
        var definition = new ExpressionDefinition(
            expression.Language,
            expression.Expression,
            targetType.ToTypeReference(),
            expression.Parameters,
            expression.Options,
            expression.CapabilityProfile,
            expression.Metadata,
            expression.VariableReadFallback);
        var effectivePolicy = ownerPolicy;
        var hasActivityResultDependency = expression.Parameters.Values
            .Any(static binding => binding is ActivityResultExpressionParameterBinding);
        var activityResultPolicyResolved = !hasActivityResultDependency;
        ExpressionEvaluationRequest request;
        RuntimeVariableReadFallback? fallback = null;
        // Spec 1228: an evaluation's warnings are kept only when it succeeds; a failed one reports its failure instead.
        var warnings = new ExecutionWarningCollector();
        try
        {
            var dependencyPolicy = DetermineDependencyPolicy(expression, resolutionContext, nodeId, inputName);
            effectivePolicy = dependencyPolicy is null
                ? ownerPolicy
                : ValuePolicyCombiner.Combine(
                    ownerPolicy,
                    dependencyPolicy,
                    $"Portable expression input '{inputName}' on executable node '{nodeId}'");
            activityResultPolicyResolved = true;

            var parameters = await MaterializeParametersAsync(expression, resolutionContext, nodeId, inputName, cancellationToken);

            // Spec 1222 FR-008, FR-013: only an expression publish marked for a computed or opaque read gets the run-time
            // lookup, and only then are the visible variables loaded. A literal-only expression reads its pinned
            // parameters and nothing else.
            fallback = expression.VariableReadFallback is null
                ? null
                : await RuntimeVariableReadFallback.LoadAsync(resolutionContext.VisibleVariables, externalPayloadStore, cancellationToken);
            request = new ExpressionEvaluationRequest(definition, parameters.Values, fallback, cancellationToken) { Warnings = warnings };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (WithheldValueException)
        {
            // The fixed VF-ACT-010 refusal names an input or a variable and carries no value, so it is rethrown as it
            // is: redacting it below would hide why the input failed.
            throw;
        }
        catch (Exception exception)
        {
            throw NewInputFailure(
                ExpressionInputFailureException.InputMaterializationFailed,
                ExpressionInputFailureException.MaterializationPhase,
                definition,
                expression,
                inputName,
                nodeId,
                effectivePolicy,
                exception,
                conservativelyRedact: !activityResultPolicyResolved);
        }

        JsonElement value;
        try
        {
            value = await portableEvaluator.EvaluateAsync(request);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (WithheldValueException)
        {
            throw;
        }
        catch (Exception exception)
        {
            // The variables the script read before it failed taint the failure (FR-019), and a refused read is the
            // fault, whatever the script made of it (FR-017a).
            if (fallback is not null)
            {
                effectivePolicy = fallback.Commit(null, effectivePolicy, nodeId, inputName);
                ThrowIfRefused(fallback, definition, expression, inputName, nodeId, effectivePolicy);
            }

            throw NewInputFailure(
                ExpressionInputFailureException.ExpressionEvaluationFailed,
                ExpressionInputFailureException.EvaluationPhaseName,
                definition,
                expression,
                inputName,
                nodeId,
                effectivePolicy,
                exception);
        }

        if (fallback is not null)
        {
            // Spec 1222 FR-010, FR-017a: the reads the script actually made join the step's input read set and taint
            // the result; a refused read faults the step even when the script caught its error.
            effectivePolicy = fallback.Commit(resolutionContext.VariableReadRecorder, effectivePolicy, nodeId, inputName);
            ThrowIfRefused(fallback, definition, expression, inputName, nodeId, effectivePolicy);
        }

        // The diagnostic names the input and the two JSON kinds only, never the value, so it needs no redaction.
        if (ShapeMismatch(targetType, value) is { } mismatch)
        {
            throw new ExpressionInputFailureException(
                ExpressionInputFailureException.InputMaterializationFailed,
                inputName,
                expression.Language,
                ExpressionInputFailureException.MaterializationPhase,
                $"Input '{inputName}' on executable node '{nodeId}' is declared '{targetType.Alias}' and accepts only a JSON {mismatch.Expected}, but its expression returned a JSON {mismatch.Actual}.");
        }

        foreach (var warning in warnings.Warnings)
            resolutionContext.Warnings?.Report(warning.WithDetail("input", inputName));
        return new PortableExpressionEvaluation(value, effectivePolicy);
    }

    // A JsonObject or JsonArray input accepts the dynamic kind at publish; the shape is checked here, at run time
    // (spec 1228 FR-021). Null is left to the input's own null rule.
    private static (string Expected, string Actual)? ShapeMismatch(ValueTypeDescriptor targetType, JsonElement value)
    {
        if (targetType.CollectionKind != CollectionKind.Single ||
            !TypeAliasConvention.IsShapedJson(targetType.Alias) ||
            value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return null;

        var expected = targetType.Alias.TrimEnd('?').EndsWith("JsonArray", StringComparison.Ordinal)
            ? JsonValueKind.Array
            : JsonValueKind.Object;
        return value.ValueKind == expected
            ? null
            : (Describe(expected), Describe(value.ValueKind));

        static string Describe(JsonValueKind kind) => kind switch
        {
            JsonValueKind.Object => "object",
            JsonValueKind.Array => "array",
            JsonValueKind.String => "string",
            JsonValueKind.Number => "number",
            _ => "boolean"
        };
    }

    private static void ThrowIfRefused(
        RuntimeVariableReadFallback fallback,
        ExpressionDefinition definition,
        RuntimeExpressionBinding expression,
        string inputName,
        string nodeId,
        ValueProtectionPolicy effectivePolicy)
    {
        try
        {
            fallback.ThrowIfRefused(nodeId, inputName);
        }
        catch (RuntimeVariableReadRefusedException refused)
        {
            throw NewInputFailure(
                ExpressionInputFailureException.ExpressionEvaluationFailed,
                ExpressionInputFailureException.EvaluationPhaseName,
                definition,
                expression,
                inputName,
                nodeId,
                effectivePolicy,
                refused);
        }
    }

    private static ExpressionInputFailureException NewInputFailure(
        string code,
        string phase,
        ExpressionDefinition definition,
        RuntimeExpressionBinding expression,
        string inputName,
        string nodeId,
        ValueProtectionPolicy effectivePolicy,
        Exception exception,
        bool conservativelyRedact = false)
    {
        var message = $"Input '{inputName}' on executable node '{nodeId}' failed to materialize or evaluate its portable '{expression.Language}' expression with fingerprint '{definition.Fingerprint}'.";
        // A failure that names only codes, variables and paths keeps its message: there is no value in it to protect.
        var cause = exception is not IValueFreeExpressionFailure && (conservativelyRedact ||
                    effectivePolicy.IsSensitive ||
                    effectivePolicy.RequiresEncryption ||
                    !string.IsNullOrWhiteSpace(effectivePolicy.RedactionMode))
            ? new RedactedPortableExpressionException(exception.GetType())
            : exception;
        return new ExpressionInputFailureException(code, inputName, expression.Language, phase, message, cause);
    }

    private static ValueProtectionPolicy? DetermineDependencyPolicy(
        RuntimeExpressionBinding expression,
        RuntimeInputBindingResolutionContext context,
        string nodeId,
        string inputName)
    {
        ValueProtectionPolicy? dependencyPolicy = null;
        foreach (var (name, binding) in expression.Parameters.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            var parameterPolicy = binding switch
            {
                LiteralExpressionParameterBinding => null,
                WorkflowRequestExpressionParameterBinding request =>
                    context.WorkflowInputEnvelopes.GetValueOrDefault(request.MemberKey)?.Policy,
                VariableExpressionParameterBinding variable =>
                    context.VariableEnvelopes.GetValueOrDefault(
                        new RuntimeVariableValueAddress(variable.DeclaringScopeNodeId, variable.VariableKey))?.Policy,
                ActivityResultExpressionParameterBinding result => DetermineActivityResultPolicy(result, context, name, nodeId, inputName),
                WorkflowInstanceExpressionParameterBinding => null,
                _ => null
            };
            if (parameterPolicy is null)
                continue;

            dependencyPolicy = dependencyPolicy is null
                ? parameterPolicy
                : ValuePolicyCombiner.Combine(
                    dependencyPolicy,
                    parameterPolicy,
                    $"Portable expression input '{inputName}' on executable node '{nodeId}'");
        }

        return dependencyPolicy;
    }

    private static ValueProtectionPolicy? DetermineActivityResultPolicy(
        ActivityResultExpressionParameterBinding binding,
        RuntimeInputBindingResolutionContext context,
        string parameterName,
        string nodeId,
        string inputName)
    {
        var consumer = context.ConsumerInvocation
            ?? throw NewParameterException(parameterName, nodeId, inputName, "requires a consumer invocation identity");
        return new CausalActivityResultResolver()
            .Resolve(binding, consumer, context.RuntimeView)?
            .Completion.Result.Policy;
    }

    /// <summary>Instance metadata comes from the resolution context, which reads it from committed state.</summary>
    private static JsonElement ReadWorkflowInstanceParameter(
        WorkflowInstanceExpressionParameterBinding binding,
        RuntimeInputBindingResolutionContext context) => binding.Member switch
    {
        WorkflowInstanceExpressionParameterBinding.Members.InstanceId => JsonSerializer.SerializeToElement(context.WorkflowExecutionId),
        _ => JsonSerializer.SerializeToElement(context.CorrelationId),
    };

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
                WorkflowInstanceExpressionParameterBinding instance => new PortableExpressionParameter(ReadWorkflowInstanceParameter(instance, context), null),
                _ => throw new InvalidOperationException($"Portable expression parameter '{name}' on input '{inputName}' uses unsupported binding type '{binding.GetType().Name}'.")
            };
            var materialized = ApplyParameterConversionPlan(expression, name, parameter, nodeId, inputName);
            values.Add(name, materialized.Value);
            if (materialized.Policy is not null)
            {
                dependencyPolicy = dependencyPolicy is null
                    ? materialized.Policy
                    : ValuePolicyCombiner.Combine(
                        dependencyPolicy,
                        materialized.Policy,
                        $"Portable expression input '{inputName}' on executable node '{nodeId}'");
            }
        }

        return new PortableExpressionParameters(values, dependencyPolicy);
    }

    private PortableExpressionParameter ApplyParameterConversionPlan(
        RuntimeExpressionBinding expression,
        string parameterName,
        PortableExpressionParameter parameter,
        string nodeId,
        string inputName)
    {
        if (!expression.ParameterConversionPlans.TryGetValue(parameterName, out var plan))
            return parameter;

        var policy = parameter.Policy ?? ValueProtectionPolicy.InstanceInline;
        var source = parameter.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
            ? ValueEnvelope.Null(plan.SourceType, policy)
            : ValueEnvelope.Inline(plan.SourceType, parameter.Value, policy);
        var converted = _valueConversionExecutor.Convert(source, plan);
        if (converted.Presence == ValuePresence.Absent)
            throw NewParameterException(parameterName, nodeId, inputName, "converted to an absent value");
        if (converted.ExternalReference is not null)
            throw NewParameterException(parameterName, nodeId, inputName, "converted to an external payload that cannot cross the portable expression boundary");
        if (converted.Policy.Lifecycle == DurableValueLifecycle.None)
            throw NewParameterException(parameterName, nodeId, inputName, "converted to a transient value that cannot cross the durable expression boundary");
        if (converted.Presence == ValuePresence.ExplicitNull)
            return new PortableExpressionParameter(JsonSerializer.SerializeToElement<object?>(null), converted.Policy);
        if (converted.InlineValue is not { } value)
            throw NewParameterException(parameterName, nodeId, inputName, "converted without an inline payload");

        return new PortableExpressionParameter(value.Clone(), converted.Policy);
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
        context.VariableReadRecorder?.Record(envelope);
        // Spec 1222 FR-006: a script reads a declared variable with no value as JSON null, never undefined.
        if (binding.IsVariablesSurface && envelope.Presence == ValuePresence.Absent && envelope.Policy.Lifecycle != DurableValueLifecycle.None)
            return new PortableExpressionParameter(JsonSerializer.SerializeToElement<object?>(null), envelope.Policy);
        return new PortableExpressionParameter(
            await ReadPersistableEnvelopeAsync(envelope, parameterName, nodeId, inputName, cancellationToken, binding.VariableKey),
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
        // Activity result projection has literal-member precedence, unlike general variable paths.
        var projected = StringComparer.Ordinal.Equals(projection.Path, "$") ? value
            : value.ValueKind == JsonValueKind.Object && value.TryGetProperty(projection.Path, out var exact) ? exact
            : ProjectPath(value, projection.Path, null, parameterName, nodeId, inputName);
        return new PortableExpressionParameter(projected, result.Policy);
    }

    private async ValueTask<JsonElement> ReadPersistableEnvelopeAsync(
        ValueEnvelope envelope,
        string parameterName,
        string nodeId,
        string inputName,
        CancellationToken cancellationToken,
        string? variableKey = null)
    {
        // The parameter needs the value, and a withheld value is not here to read. The input itself is not withheld:
        // the variable, workflow request member or activity result it reads is.
        if (envelope.Presence == ValuePresence.Withheld)
            throw variableKey is null
                ? SecretBindingDiagnostics.WithheldSourceNotResolved(inputName)
                : SecretBindingDiagnostics.WithheldVariableNotResolved(variableKey);
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

internal sealed class RedactedPortableExpressionException(Type evaluatorExceptionType)
    : Exception($"Portable expression evaluator failure of type '{evaluatorExceptionType.FullName ?? evaluatorExceptionType.Name}' was redacted by value-protection policy.");

internal sealed record PortableExpressionEvaluation(JsonElement Value, ValueProtectionPolicy EffectivePolicy);

internal sealed record PortableExpressionParameters(
    IReadOnlyDictionary<string, JsonElement> Values,
    ValueProtectionPolicy? DependencyPolicy);

internal sealed record PortableExpressionParameter(JsonElement Value, ValueProtectionPolicy? Policy);
