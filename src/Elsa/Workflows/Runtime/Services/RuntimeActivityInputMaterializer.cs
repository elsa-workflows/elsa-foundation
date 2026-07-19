using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Expressions.Core.Constants;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Resolvers;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class RuntimeActivityInputMaterializer : IRuntimeActivityInputMaterializer
{
    private readonly IRuntimeInputBindingResolver _inputBindingResolver;
    private readonly IWellKnownTypeRegistry? _wellKnownTypeRegistry;
    private readonly IExternalPayloadStore? _externalPayloadStore;
    private readonly IRuntimeValueConversionExecutor _valueConversionExecutor;
    private readonly RuntimePortableExpressionEvaluator _portableExpressionEvaluator;
    private readonly RuntimeExternalEnvelopeStorage _externalEnvelopeStorage;

    public RuntimeActivityInputMaterializer()
        : this(new RuntimeInputBindingResolver())
    {
    }

    public RuntimeActivityInputMaterializer(IRuntimeInputBindingResolver inputBindingResolver)
    {
        ArgumentNullException.ThrowIfNull(inputBindingResolver);

        _inputBindingResolver = inputBindingResolver;
        _valueConversionExecutor = new RuntimeValueConversionExecutor();
        _portableExpressionEvaluator = new RuntimePortableExpressionEvaluator(portableExpressionEvaluator: null, externalPayloadStore: null);
        _externalEnvelopeStorage = new RuntimeExternalEnvelopeStorage(externalPayloadStore: null);
    }

    public RuntimeActivityInputMaterializer(
        IRuntimeInputBindingResolver inputBindingResolver,
        IExternalPayloadStore externalPayloadStore)
    {
        ArgumentNullException.ThrowIfNull(inputBindingResolver);
        ArgumentNullException.ThrowIfNull(externalPayloadStore);

        _inputBindingResolver = inputBindingResolver;
        _valueConversionExecutor = new RuntimeValueConversionExecutor();
        _externalPayloadStore = externalPayloadStore;
        _portableExpressionEvaluator = new RuntimePortableExpressionEvaluator(portableExpressionEvaluator: null, externalPayloadStore);
        _externalEnvelopeStorage = new RuntimeExternalEnvelopeStorage(externalPayloadStore);
    }

    public RuntimeActivityInputMaterializer(
        IRuntimeInputBindingResolver inputBindingResolver,
        IWellKnownTypeRegistry wellKnownTypeRegistry,
        IExternalPayloadStore? externalPayloadStore = null)
    {
        ArgumentNullException.ThrowIfNull(inputBindingResolver);
        ArgumentNullException.ThrowIfNull(wellKnownTypeRegistry);

        _inputBindingResolver = inputBindingResolver;
        _valueConversionExecutor = new RuntimeValueConversionExecutor(wellKnownTypeRegistry);
        _wellKnownTypeRegistry = wellKnownTypeRegistry;
        _externalPayloadStore = externalPayloadStore;
        _portableExpressionEvaluator = new RuntimePortableExpressionEvaluator(portableExpressionEvaluator: null, externalPayloadStore);
        _externalEnvelopeStorage = new RuntimeExternalEnvelopeStorage(externalPayloadStore);
    }

    public RuntimeActivityInputMaterializer(
        IRuntimeInputBindingResolver inputBindingResolver,
        IWellKnownTypeRegistry wellKnownTypeRegistry,
        IPortableExpressionEvaluator portableExpressionEvaluator,
        IExternalPayloadStore? externalPayloadStore = null)
    {
        ArgumentNullException.ThrowIfNull(inputBindingResolver);
        ArgumentNullException.ThrowIfNull(wellKnownTypeRegistry);
        ArgumentNullException.ThrowIfNull(portableExpressionEvaluator);

        _inputBindingResolver = inputBindingResolver;
        _valueConversionExecutor = new RuntimeValueConversionExecutor(wellKnownTypeRegistry);
        _wellKnownTypeRegistry = wellKnownTypeRegistry;
        _externalPayloadStore = externalPayloadStore;
        _portableExpressionEvaluator = new RuntimePortableExpressionEvaluator(portableExpressionEvaluator, externalPayloadStore);
        _externalEnvelopeStorage = new RuntimeExternalEnvelopeStorage(externalPayloadStore);
    }

    public RuntimeActivityInputMaterializer(
        IRuntimeInputBindingResolver inputBindingResolver,
        IWellKnownTypeRegistry wellKnownTypeRegistry,
        IPortableExpressionEvaluator portableExpressionEvaluator,
        IRuntimeValueConversionExecutor valueConversionExecutor,
        IExternalPayloadStore? externalPayloadStore = null)
    {
        ArgumentNullException.ThrowIfNull(inputBindingResolver);
        ArgumentNullException.ThrowIfNull(wellKnownTypeRegistry);
        ArgumentNullException.ThrowIfNull(portableExpressionEvaluator);
        ArgumentNullException.ThrowIfNull(valueConversionExecutor);

        _inputBindingResolver = inputBindingResolver;
        _wellKnownTypeRegistry = wellKnownTypeRegistry;
        _externalPayloadStore = externalPayloadStore;
        _valueConversionExecutor = valueConversionExecutor;
        _portableExpressionEvaluator = new RuntimePortableExpressionEvaluator(portableExpressionEvaluator, externalPayloadStore);
        _externalEnvelopeStorage = new RuntimeExternalEnvelopeStorage(externalPayloadStore);
    }

    public async ValueTask<ActivityInputSnapshot> MaterializeSnapshotAsync(
        ExecutableNode node,
        string invocationId,
        RuntimeInputBindingResolutionContext resolutionContext,
        DateTimeOffset materializedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentException.ThrowIfNullOrWhiteSpace(invocationId);
        ArgumentNullException.ThrowIfNull(resolutionContext);

        var contract = node.ActivityContract
            ?? throw new InvalidOperationException($"Executable node '{node.ExecutableNodeId}' has no pinned activity contract.");
        ValidateCompleteBindingSet(node, contract);

        var values = new Dictionary<string, ValueEnvelope>(StringComparer.Ordinal);
        foreach (var input in contract.Inputs.Values.OrderBy(input => input.Key, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!node.InputBindings.TryGetValue(input.Key, out var binding))
                throw new InvalidOperationException($"VF-ACT-003: Input '{input.Key}' on executable node '{node.ExecutableNodeId}' is absent after binding normalization.");
            ValidatePortableType(input, binding, node.ExecutableNodeId);
            ValidateEffectivePolicy(input, binding, node.ExecutableNodeId);

            var value = await MaterializeEnvelopeAsync(
                node,
                invocationId,
                input,
                binding,
                resolutionContext,
                cancellationToken);

            ValidatePresence(input, value, node.ExecutableNodeId);
            values.Add(input.Key, value);
        }

        return new ActivityInputSnapshot(
            invocationId,
            contract.SchemaFingerprint,
            ComputeBindingFingerprint(node),
            values,
            materializedAt);
    }

    private async ValueTask<ValueEnvelope> MaterializeEnvelopeAsync(
        ExecutableNode node,
        string invocationId,
        ActivityInputContract input,
        RuntimeInputBinding binding,
        RuntimeInputBindingResolutionContext resolutionContext,
        CancellationToken cancellationToken)
    {
        var resolved = _inputBindingResolver.Resolve(binding, resolutionContext);
        if (resolved.Source == RuntimeInputBindingSource.Expression)
        {
            var type = ResolveInputType(binding, node.ExecutableNodeId, input.Key);
            var evaluated = await EvaluateExpressionAsync(
                resolved,
                type,
                binding.TargetType,
                binding.EffectivePolicy,
                node.ExecutableNodeId,
                input.Key,
                resolutionContext,
                cancellationToken);
            var materialized = CoerceToType(
                evaluated.Value,
                type);
            var expressionPolicy = evaluated.EffectivePolicy ?? binding.EffectivePolicy;
            var envelope = materialized is null
                ? ValueEnvelope.Null(binding.TargetType, expressionPolicy)
                : ValueEnvelope.Inline(
                    binding.TargetType,
                    JsonSerializer.SerializeToElement(materialized, materialized.GetType()),
                    expressionPolicy);
            return await ApplyDestinationStorageAsync(
                invocationId,
                input,
                ApplyConversionPlan(binding, envelope),
                expressionPolicy,
                expressionPolicy,
                resolutionContext,
                cancellationToken);
        }

        var source = resolved.Envelope;
        if (source is null)
        {
            throw new InvalidOperationException(
                $"VF-ACT-005: Canonical input '{input.Key}' on executable node '{node.ExecutableNodeId}' " +
                "was resolved without its source protection envelope.");
        }

        if (binding.Source == RuntimeInputBindingSource.Literal && !SameType(source.Type, binding.TargetType) &&
            (binding.ConversionPlan is null || !SameType(source.Type, binding.ConversionPlan.SourceType)))
            throw new InvalidOperationException($"VF-ACT-004: Literal input '{input.Key}' on executable node '{node.ExecutableNodeId}' does not match its declared portable type.");
        if (source.Policy.Lifecycle == DurableValueLifecycle.None)
        {
            if (binding.EffectivePolicy.Lifecycle != DurableValueLifecycle.None || input.Policy.IsPersistable)
                throw new InvalidOperationException(
                    $"VF-ACT-005: Input '{input.Key}' on executable node '{node.ExecutableNodeId}' has source representation '{ValueRepresentation.TransientResource}' " +
                    $"but destination storage policy '{binding.EffectivePolicy.Storage}' requires a durable invocation boundary. " +
                    "Use an execution-local non-persistable input binding for live resources, or model an explicit DurableReference/resource-handle.");

            return binding.ConversionPlan is null
                ? source.Retype(binding.TargetType)
                : ApplyConversionPlan(binding, source);
        }
        var effectivePolicy = ValuePolicyCombiner.Combine(
            binding.EffectivePolicy,
            source.Policy,
            $"Input '{input.Key}' on executable node '{node.ExecutableNodeId}'");

        if (source.Presence == ValuePresence.Absent && input.IsRequired)
            throw new InvalidOperationException($"VF-ACT-003: Required input '{input.Key}' on executable node '{node.ExecutableNodeId}' cannot materialize as absent.");

        if (source.ExternalReference is not null && HasExternalProjection(binding))
            return await MaterializeExternalProjectionAsync(node, invocationId, input, binding, source, effectivePolicy, resolutionContext, cancellationToken);

        var projected = source;
        var retyped = binding.ConversionPlan is null
            ? new ValueEnvelope(
                binding.TargetType,
                projected.Presence,
                projected.InlineValue,
                projected.ExternalReference,
                effectivePolicy)
            : ApplyConversionPlan(binding, new ValueEnvelope(
                projected.Type,
                projected.Presence,
                projected.InlineValue,
                projected.ExternalReference,
                effectivePolicy));
        return await ApplyDestinationStorageAsync(
            invocationId,
            input,
            retyped,
            source.Policy,
            effectivePolicy,
            resolutionContext,
            cancellationToken);
    }

    private async ValueTask<ValueEnvelope> ApplyDestinationStorageAsync(
        string invocationId,
        ActivityInputContract input,
        ValueEnvelope value,
        ValueProtectionPolicy sourcePolicy,
        ValueProtectionPolicy effectivePolicy,
        RuntimeInputBindingResolutionContext resolutionContext,
        CancellationToken cancellationToken)
    {
        return await _externalEnvelopeStorage.RewriteAsync(
            new RuntimeExternalEnvelopeRewriteRequest(
                resolutionContext.WorkflowExecutionId,
                $"activity:{invocationId}:input:{input.Key}",
                $"Input '{input.Key}'",
                value,
                sourcePolicy,
                effectivePolicy),
            cancellationToken);
    }

    private async ValueTask<ValueEnvelope> MaterializeExternalProjectionAsync(
        ExecutableNode node,
        string invocationId,
        ActivityInputContract input,
        RuntimeInputBinding binding,
        ValueEnvelope source,
        ValueProtectionPolicy effectivePolicy,
        RuntimeInputBindingResolutionContext resolutionContext,
        CancellationToken cancellationToken)
    {
        if (_externalPayloadStore is null)
            throw new InvalidOperationException($"VF-ACT-005: Input '{input.Key}' on executable node '{node.ExecutableNodeId}' requires an IExternalPayloadStore for projection.");

        var payload = await _externalPayloadStore.ReadAsync(source.ExternalReference!, cancellationToken);
        var path = ResolveProjectionPath(binding, resolutionContext);
        foreach (var segment in path.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (payload.ValueKind != JsonValueKind.Object || !TryGetProperty(payload, segment, out payload))
                throw new InvalidOperationException($"VF-ACT-004: External source path '{path}' for input '{input.Key}' on executable node '{node.ExecutableNodeId}' is unavailable.");
        }

        if (payload.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return ValueEnvelope.Null(binding.ConversionPlan?.TargetType ?? binding.TargetType, effectivePolicy);

        var projected = ValueEnvelope.Inline(binding.ConversionPlan?.SourceType ?? binding.TargetType, payload.Clone(), effectivePolicy);
        var converted = ApplyConversionPlan(binding, projected);
        return await _externalEnvelopeStorage.RewriteAsync(
            new RuntimeExternalEnvelopeRewriteRequest(
                resolutionContext.WorkflowExecutionId,
                $"activity:{invocationId}:input:{input.Key}",
                $"Input '{input.Key}' on executable node '{node.ExecutableNodeId}'",
                converted,
                source.Policy,
                effectivePolicy,
                source.ExternalReference!.StorageProfile,
                ForceExternal: true),
            cancellationToken);
    }

    private ValueEnvelope ApplyConversionPlan(RuntimeInputBinding binding, ValueEnvelope source) =>
        binding.ConversionPlan is null
            ? source
            : _valueConversionExecutor.Convert(source, binding.ConversionPlan);

    private static bool HasExternalProjection(RuntimeInputBinding binding) =>
        binding.Source == RuntimeInputBindingSource.ActivityResult &&
        !StringComparer.Ordinal.Equals(binding.ActivityResult!.ProjectionKey, "$result") ||
        binding.Source == RuntimeInputBindingSource.WorkflowRequest &&
        !string.IsNullOrWhiteSpace(binding.WorkflowRequest!.Path);

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

    private static string ResolveProjectionPath(
        RuntimeInputBinding binding,
        RuntimeInputBindingResolutionContext resolutionContext)
    {
        if (binding.Source == RuntimeInputBindingSource.WorkflowRequest)
        {
            var reference = binding.WorkflowRequest!;
            var segments = reference.Path!.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var offset = segments.Length > 0 && StringComparer.OrdinalIgnoreCase.Equals(segments[0], reference.MemberKey) ? 1 : 0;
            return string.Join('.', segments.Skip(offset));
        }

        var resultReference = binding.ActivityResult!;
        var producer = resolutionContext.Executable?.NodesById.GetValueOrDefault(resultReference.ProducerExecutableNodeId)
            ?? throw new InvalidOperationException($"External activity result input '{binding.InputName}' requires the pinned producer executable contract.");
        return producer.ActivityContract?.Result.Projections.GetValueOrDefault(resultReference.ProjectionKey)?.Path
            ?? throw new InvalidOperationException($"Producer node '{resultReference.ProducerExecutableNodeId}' has no result projection '{resultReference.ProjectionKey}'.");
    }

    private static void ValidateCompleteBindingSet(ExecutableNode node, ActivityContract contract)
    {
        var missing = contract.Inputs.Values
            .Where(input => !node.InputBindings.ContainsKey(input.Key))
            .Select(input => input.Key)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var unknown = node.InputBindings.Keys.Except(contract.Inputs.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

        if (missing.Length == 0 && unknown.Length == 0)
            return;

        throw new InvalidOperationException(
            $"VF-ACT-003: Executable node '{node.ExecutableNodeId}' cannot materialize a complete input snapshot. " +
            $"Missing keys: [{string.Join(", ", missing)}]; unknown keys: [{string.Join(", ", unknown)}].");
    }

    private static void ValidatePortableType(ActivityInputContract input, RuntimeInputBinding binding, string nodeId)
    {
        if (SameType(input.Type, binding.TargetType))
            return;

        throw new InvalidOperationException(
            $"VF-ACT-004: Input '{input.Key}' on executable node '{nodeId}' has portable type " +
            $"'{binding.TargetType.Alias}', but contract '{input.Type.Alias}' is pinned.");
    }

    private static void ValidateEffectivePolicy(ActivityInputContract input, RuntimeInputBinding binding, string nodeId)
    {
        var policy = binding.EffectivePolicy;
        if (!input.Policy.IsPersistable)
        {
            if (policy.Lifecycle != DurableValueLifecycle.None)
            {
                throw new InvalidOperationException(
                    $"VF-ACT-005: Input '{input.Key}' on executable node '{nodeId}' is non-persistable but binding destination storage policy '{policy.Storage}' is durable.");
            }

            return;
        }

        if (policy.Lifecycle == DurableValueLifecycle.None)
            throw new InvalidOperationException($"VF-ACT-005: Input '{input.Key}' on executable node '{nodeId}' is not persistable at the durable ActivityStarted boundary.");

        if (!policy.Satisfies(ValuePolicyCombiner.ToProtectionPolicy(input.Policy)))
            throw new InvalidOperationException($"VF-ACT-005: Input '{input.Key}' on executable node '{nodeId}' would downgrade its contract protection policy.");
    }

    private static void ValidatePresence(ActivityInputContract input, ValueEnvelope value, string nodeId)
    {
        if (value.Presence == ValuePresence.Absent && input.IsRequired)
            throw new InvalidOperationException($"VF-ACT-003: Required input '{input.Key}' on executable node '{nodeId}' cannot materialize as absent.");

        if (value.Presence is ValuePresence.Absent or ValuePresence.ExplicitNull && input.IsNullable is false)
            throw new InvalidOperationException($"VF-ACT-004: Input '{input.Key}' on executable node '{nodeId}' does not accept null or absence.");
    }

    private static bool SameType(ValueTypeDescriptor left, ValueTypeDescriptor right) =>
        StringComparer.Ordinal.Equals(left.Alias, right.Alias) &&
        left.CollectionKind == right.CollectionKind &&
        left.SchemaVersion == right.SchemaVersion &&
        StringComparer.Ordinal.Equals(left.Schema?.GetRawText(), right.Schema?.GetRawText());

    private static string ComputeBindingFingerprint(ExecutableNode node)
    {
        var canonical = JsonSerializer.Serialize(node.InputBindings
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .Select(item => new
            {
                key = item.Key,
                item.Value.TargetType,
                item.Value.EffectivePolicy,
                item.Value.Source,
                item.Value.Literal,
                item.Value.WorkflowRequest,
                item.Value.Variable,
                item.Value.ActivityResult,
                item.Value.Expression,
                metadata = item.Value.Metadata.OrderBy(metadata => metadata.Key, StringComparer.Ordinal)
            }));
        return $"sha256:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant()}";
    }

    private async ValueTask<EvaluatedExpression> EvaluateExpressionAsync(
        RuntimeResolvedInput resolved,
        Type type,
        ValueTypeDescriptor targetType,
        ValueProtectionPolicy bindingPolicy,
        string nodeId,
        string inputName,
        RuntimeInputBindingResolutionContext resolutionContext,
        CancellationToken cancellationToken)
    {
        var expression = resolved.Expression
            ?? throw new InvalidOperationException($"Input '{inputName}' on executable node '{nodeId}' is an expression binding without an expression payload.");

        if (string.Equals(expression.Language, WellKnownExpressionDescriptorTypes.Object, StringComparison.Ordinal))
            return new EvaluatedExpression(DeserializeObjectExpression(expression, type, nodeId, inputName), null);

        var evaluated = await _portableExpressionEvaluator.EvaluateAsync(
            expression,
            targetType,
            bindingPolicy,
            nodeId,
            inputName,
            resolutionContext,
            cancellationToken);
        return new EvaluatedExpression(evaluated.Value, evaluated.EffectivePolicy);
    }

    private static object? DeserializeObjectExpression(RuntimeExpressionBinding expression, Type type, string nodeId, string inputName)
    {
        try
        {
            return JsonSerializer.Deserialize(expression.Expression, type);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"Input '{inputName}' on executable node '{nodeId}' carries an invalid '{expression.Language}' expression payload.", exception);
        }
    }

    /// <summary>
    /// Coerces an evaluated expression result to the input's declared <paramref name="type"/> when it
    /// is a <see cref="JsonElement"/>. Scoped variable values round-trip through the container
    /// execution's persisted snapshot as JSON, so a non-string variable (e.g. an <c>int</c>) assigned
    /// by one activity and read by a sibling (or after resume) arrives here as a <see cref="JsonElement"/>;
    /// without this it would reach the activity as a boxed element rather than the declared CLR type.
    /// Non-<see cref="JsonElement"/> results (the usual literal/script case) are returned unchanged.
    /// </summary>
    private static object? CoerceToType(object? value, Type type)
    {
        if (value is not JsonElement element)
            return value;

        return element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
            ? null
            : JsonSerializer.Deserialize(element.GetRawText(), type);
    }

    private Type ResolveInputType(RuntimeInputBinding binding, string nodeId, string inputName)
    {
        if (binding.TargetType.Alias == "Elsa.Any")
            return typeof(object);
        if (_wellKnownTypeRegistry is null)
            throw new InvalidOperationException($"Input '{inputName}' on executable node '{nodeId}' declares portable type alias '{binding.TargetType.Alias}', but no well-known type registry was supplied.");

        var typeReference = binding.TargetType.ToTypeReference();
        var resolvedType = TypeReferenceFactory.Resolve(
            typeReference,
            alias => _wellKnownTypeRegistry.TryGetTypeOrDefault(alias, out var type) ? type : typeof(object));

        if (resolvedType == typeof(object) && !string.Equals(binding.TargetType.Alias, "Object", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException($"Input '{inputName}' on executable node '{nodeId}' declares unknown portable type alias '{binding.TargetType.Alias}'.");

        return resolvedType;
    }
}

internal sealed record EvaluatedExpression(object? Value, ValueProtectionPolicy? EffectivePolicy);
