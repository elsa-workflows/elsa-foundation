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

    public RuntimeActivityInputMaterializer()
        : this(new RuntimeInputBindingResolver())
    {
    }

    public RuntimeActivityInputMaterializer(IRuntimeInputBindingResolver inputBindingResolver)
    {
        ArgumentNullException.ThrowIfNull(inputBindingResolver);

        _inputBindingResolver = inputBindingResolver;
    }

    public RuntimeActivityInputMaterializer(
        IRuntimeInputBindingResolver inputBindingResolver,
        IExternalPayloadStore externalPayloadStore)
    {
        ArgumentNullException.ThrowIfNull(inputBindingResolver);
        ArgumentNullException.ThrowIfNull(externalPayloadStore);

        _inputBindingResolver = inputBindingResolver;
        _externalPayloadStore = externalPayloadStore;
    }

    public RuntimeActivityInputMaterializer(
        IRuntimeInputBindingResolver inputBindingResolver,
        IWellKnownTypeRegistry wellKnownTypeRegistry,
        IExternalPayloadStore? externalPayloadStore = null)
    {
        ArgumentNullException.ThrowIfNull(inputBindingResolver);
        ArgumentNullException.ThrowIfNull(wellKnownTypeRegistry);

        _inputBindingResolver = inputBindingResolver;
        _wellKnownTypeRegistry = wellKnownTypeRegistry;
        _externalPayloadStore = externalPayloadStore;
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
            var binding = node.InputBindings[input.Key];
            ValidatePortableType(input, binding, node.ExecutableNodeId);
            ValidateEffectivePolicy(input, binding, node.ExecutableNodeId);

            var value = await MaterializeEnvelopeAsync(
                node,
                invocationId,
                input,
                binding,
                resolutionContext,
                cancellationToken);

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
            var materialized = CoerceToType(
                await EvaluateExpressionAsync(resolved, type, binding.TargetType, node.ExecutableNodeId, input.Key, resolutionContext, cancellationToken),
                type);
            var envelope = materialized is null
                ? ValueEnvelope.Null(binding.TargetType, binding.EffectivePolicy)
                : ValueEnvelope.Inline(
                    binding.TargetType,
                    JsonSerializer.SerializeToElement(materialized, materialized.GetType()),
                    binding.EffectivePolicy);
            return await ApplyDestinationStorageAsync(invocationId, input, binding, envelope, resolutionContext, cancellationToken);
        }

        var source = resolved.Envelope;
        if (source is null)
        {
            throw new InvalidOperationException(
                $"VF-ACT-005: Canonical input '{input.Key}' on executable node '{node.ExecutableNodeId}' " +
                "was resolved without its source protection envelope.");
        }

        if (binding.Source == RuntimeInputBindingSource.Literal && !SameType(source.Type, binding.TargetType))
            throw new InvalidOperationException($"VF-ACT-004: Literal input '{input.Key}' on executable node '{node.ExecutableNodeId}' does not match its declared portable type.");
        if (source.Presence == ValuePresence.Absent)
            throw new InvalidOperationException($"VF-ACT-003: Input '{input.Key}' on executable node '{node.ExecutableNodeId}' is absent after binding normalization.");
        if (source.Policy.Lifecycle == DurableValueLifecycle.None || !binding.EffectivePolicy.Satisfies(source.Policy))
            throw new InvalidOperationException($"VF-ACT-005: Input '{input.Key}' on executable node '{node.ExecutableNodeId}' would downgrade its source persistence or protection policy.");

        if (source.ExternalReference is not null && HasExternalProjection(binding))
            return await MaterializeExternalProjectionAsync(node, invocationId, input, binding, source, resolutionContext, cancellationToken);

        var retyped = new ValueEnvelope(
            binding.TargetType,
            source.Presence,
            source.InlineValue,
            source.ExternalReference,
            binding.EffectivePolicy);
        return await ApplyDestinationStorageAsync(invocationId, input, binding, retyped, resolutionContext, cancellationToken);
    }

    private async ValueTask<ValueEnvelope> ApplyDestinationStorageAsync(
        string invocationId,
        ActivityInputContract input,
        RuntimeInputBinding binding,
        ValueEnvelope value,
        RuntimeInputBindingResolutionContext resolutionContext,
        CancellationToken cancellationToken)
    {
        if (value.Presence != ValuePresence.Present || binding.EffectivePolicy.Storage == DurableValueStorage.Inline)
            return value;
        if (binding.EffectivePolicy.Storage == DurableValueStorage.Custom)
            throw new InvalidOperationException($"VF-ACT-005: Input '{input.Key}' requires custom storage, but no canonical custom value-storage seam is configured.");

        var storageProfile = RequireStorageProfile(binding.EffectivePolicy, input.Key, value.ExternalReference?.StorageProfile);
        if (value.ExternalReference is { } existing && StringComparer.Ordinal.Equals(existing.StorageProfile, storageProfile))
            return value;
        if (_externalPayloadStore is null)
            throw new InvalidOperationException($"VF-ACT-005: Input '{input.Key}' requires an IExternalPayloadStore.");

        var payload = value.InlineValue ?? await _externalPayloadStore.ReadAsync(value.ExternalReference!, cancellationToken);
        var reference = await _externalPayloadStore.WriteAsync(
            new ExternalPayloadWriteRequest(
                resolutionContext.WorkflowExecutionId,
                $"activity:{invocationId}:input:{input.Key}",
                storageProfile,
                binding.TargetType,
                payload.Clone(),
                binding.EffectivePolicy),
            cancellationToken);
        return ValueEnvelope.External(binding.TargetType, reference, binding.EffectivePolicy);
    }

    private async ValueTask<ValueEnvelope> MaterializeExternalProjectionAsync(
        ExecutableNode node,
        string invocationId,
        ActivityInputContract input,
        RuntimeInputBinding binding,
        ValueEnvelope source,
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
            return ValueEnvelope.Null(binding.TargetType, binding.EffectivePolicy);

        var reference = await _externalPayloadStore.WriteAsync(
            new ExternalPayloadWriteRequest(
                resolutionContext.WorkflowExecutionId,
                $"activity:{invocationId}:input:{input.Key}",
                RequireStorageProfile(binding.EffectivePolicy, input.Key, source.ExternalReference!.StorageProfile),
                binding.TargetType,
                payload.Clone(),
                binding.EffectivePolicy),
            cancellationToken);
        return ValueEnvelope.External(binding.TargetType, reference, binding.EffectivePolicy);
    }

    private static string RequireStorageProfile(ValueProtectionPolicy policy, string inputKey, string? fallback = null) =>
        policy.Metadata.TryGetValue(ValuePolicyCombiner.StorageProfileMetadataKey, out var profile) && !string.IsNullOrWhiteSpace(profile)
            ? profile
            : !string.IsNullOrWhiteSpace(fallback)
                ? fallback
                : throw new InvalidOperationException($"VF-ACT-005: External input '{inputKey}' has no storage profile.");

    private static bool HasExternalProjection(RuntimeInputBinding binding) =>
        binding.Source == RuntimeInputBindingSource.ActivityResult &&
        !StringComparer.Ordinal.Equals(binding.ActivityResult!.ProjectionKey, "$result") ||
        binding.Source == RuntimeInputBindingSource.WorkflowRequest &&
        !string.IsNullOrWhiteSpace(binding.WorkflowRequest!.Path);

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
        var missing = contract.Inputs.Keys.Except(node.InputBindings.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
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
        if (!input.Policy.IsPersistable || policy.Lifecycle == DurableValueLifecycle.None)
            throw new InvalidOperationException($"VF-ACT-005: Input '{input.Key}' on executable node '{nodeId}' is not persistable at the durable ActivityStarted boundary.");

        if (!policy.Satisfies(ValuePolicyCombiner.ToProtectionPolicy(input.Policy)))
            throw new InvalidOperationException($"VF-ACT-005: Input '{input.Key}' on executable node '{nodeId}' would downgrade its contract protection policy.");
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

    private static async ValueTask<object?> EvaluateExpressionAsync(
        RuntimeResolvedInput resolved,
        Type type,
        ValueTypeDescriptor targetType,
        string nodeId,
        string inputName,
        RuntimeInputBindingResolutionContext resolutionContext,
        CancellationToken cancellationToken)
    {
        var expression = resolved.Expression
            ?? throw new InvalidOperationException($"Input '{inputName}' on executable node '{nodeId}' is an expression binding without an expression payload.");

        if (string.Equals(expression.Language, WellKnownExpressionDescriptorTypes.Object, StringComparison.Ordinal))
            return DeserializeObjectExpression(expression, type, nodeId, inputName);

        return await EvaluatePortableExpressionAsync(expression, targetType, nodeId, inputName, resolutionContext, cancellationToken);
    }

    /// <summary>
    /// Evaluates one canonical expression binding from its closed parameter snapshot. This is shared
    /// by activity input materialization and engine intrinsics; it never constructs or accepts an
    /// <see cref="IExpressionExecutionContext"/>.
    /// </summary>
    internal static async ValueTask<JsonElement> EvaluatePortableExpressionAsync(
        RuntimeExpressionBinding expression,
        ValueTypeDescriptor targetType,
        string nodeId,
        string inputName,
        RuntimeInputBindingResolutionContext resolutionContext,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(expression);
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
        var request = new ExpressionEvaluationRequest(
            definition,
            MaterializePortableParameters(expression, resolutionContext, nodeId, inputName),
            cancellationToken);

        try
        {
            return await portableEvaluator.EvaluateAsync(request);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"Input '{inputName}' on executable node '{nodeId}' failed to evaluate its portable '{expression.Language}' expression.", exception);
        }
    }

    private static IReadOnlyDictionary<string, JsonElement> MaterializePortableParameters(
        RuntimeExpressionBinding expression,
        RuntimeInputBindingResolutionContext context,
        string nodeId,
        string inputName)
    {
        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var (name, binding) in expression.Parameters.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            values.Add(name, binding switch
            {
                LiteralExpressionParameterBinding literal => ReadLiteralParameter(literal, name, nodeId, inputName),
                WorkflowRequestExpressionParameterBinding request => ReadWorkflowRequestParameter(request, context, name, nodeId, inputName),
                VariableExpressionParameterBinding variable => ReadVariableParameter(variable, context, name, nodeId, inputName),
                ActivityResultExpressionParameterBinding result => ReadActivityResultParameter(result, context, name, nodeId, inputName),
                _ => throw new InvalidOperationException($"Portable expression parameter '{name}' on input '{inputName}' uses unsupported binding type '{binding.GetType().Name}'.")
            });
        }

        return values;
    }

    private static JsonElement ReadLiteralParameter(
        LiteralExpressionParameterBinding binding,
        string parameterName,
        string nodeId,
        string inputName)
    {
        if (binding.Value.ValueKind == JsonValueKind.Undefined)
            throw NewPortableParameterException(parameterName, nodeId, inputName, "has an undefined literal");
        return binding.Value.Clone();
    }

    private static JsonElement ReadWorkflowRequestParameter(
        WorkflowRequestExpressionParameterBinding binding,
        RuntimeInputBindingResolutionContext context,
        string parameterName,
        string nodeId,
        string inputName)
    {
        if (!context.WorkflowInputEnvelopes.TryGetValue(binding.MemberKey, out var envelope))
            throw NewPortableParameterException(parameterName, nodeId, inputName, $"references unavailable persistable workflow request member '{binding.MemberKey}'");

        var value = ReadPersistableEnvelope(envelope, parameterName, nodeId, inputName);
        return ProjectPath(value, binding.Path, binding.MemberKey, parameterName, nodeId, inputName);
    }

    private static JsonElement ReadVariableParameter(
        VariableExpressionParameterBinding binding,
        RuntimeInputBindingResolutionContext context,
        string parameterName,
        string nodeId,
        string inputName)
    {
        var address = new RuntimeVariableValueAddress(binding.DeclaringScopeNodeId, binding.VariableKey);
        if (!context.VariableEnvelopes.TryGetValue(address, out var envelope))
            throw NewPortableParameterException(parameterName, nodeId, inputName, $"references unavailable variable '{binding.VariableKey}' in scope '{binding.DeclaringScopeNodeId}'");
        return ReadPersistableEnvelope(envelope, parameterName, nodeId, inputName);
    }

    private static JsonElement ReadActivityResultParameter(
        ActivityResultExpressionParameterBinding binding,
        RuntimeInputBindingResolutionContext context,
        string parameterName,
        string nodeId,
        string inputName)
    {
        var consumer = context.ConsumerInvocation
            ?? throw NewPortableParameterException(parameterName, nodeId, inputName, "requires a consumer invocation identity");
        var resolution = new CausalActivityResultResolver().Resolve(binding, consumer, context.RuntimeView);
        if (resolution is null)
            return JsonSerializer.SerializeToElement<object?>(null);

        var result = resolution.Completion.Result;
        var value = ReadPersistableEnvelope(result, parameterName, nodeId, inputName);
        if (StringComparer.Ordinal.Equals(binding.ProjectionKey, "$result"))
            return value;

        var producerNode = context.Executable?.NodesById.GetValueOrDefault(binding.ProducerNodeId)
            ?? throw NewPortableParameterException(parameterName, nodeId, inputName, $"requires the pinned contract for producer node '{binding.ProducerNodeId}'");
        var projection = producerNode.ActivityContract?.Result.Projections.GetValueOrDefault(binding.ProjectionKey)
            ?? throw NewPortableParameterException(parameterName, nodeId, inputName, $"references unknown result projection '{binding.ProjectionKey}' on producer node '{binding.ProducerNodeId}'");
        return ProjectPath(value, projection.Path, null, parameterName, nodeId, inputName);
    }

    private static JsonElement ReadPersistableEnvelope(
        ValueEnvelope envelope,
        string parameterName,
        string nodeId,
        string inputName)
    {
        if (envelope.Policy.Lifecycle == DurableValueLifecycle.None)
            throw NewPortableParameterException(parameterName, nodeId, inputName, "is transient and cannot cross the durable expression boundary");
        if (envelope.Presence == ValuePresence.Absent)
            throw NewPortableParameterException(parameterName, nodeId, inputName, "is absent");
        if (envelope.Presence == ValuePresence.ExplicitNull)
            return JsonSerializer.SerializeToElement<object?>(null);
        if (!envelope.InlineValue.HasValue)
            throw NewPortableParameterException(parameterName, nodeId, inputName, "uses external storage without a payload reader");
        return envelope.InlineValue.Value.Clone();
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
                throw NewPortableParameterException(parameterName, nodeId, inputName, $"has unavailable path '{path}'");
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

    private static InvalidOperationException NewPortableParameterException(
        string parameterName,
        string nodeId,
        string inputName,
        string reason) =>
        new($"Portable expression parameter '{parameterName}' for input '{inputName}' on executable node '{nodeId}' {reason}.");

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
