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
    public const string InputTypeMetadataKey = "typeName";
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

    public ValueTask<IReadOnlyList<RuntimeMaterializedActivityInput>> MaterializeInputsAsync(
        ExecutableNode node,
        IServiceProvider? serviceProvider = null,
        CancellationToken cancellationToken = default) =>
        MaterializeInputsAsync(
            node,
            new RuntimeInputBindingResolutionContext(
                workflowExecutionId: "literal-only",
                activityExecutionId: "literal-only",
                durableValuesByValueId: new Dictionary<string, DurableValueState>(),
                activityOutputs: EmptyRuntimeActivityOutputReader.Instance,
                serviceProvider: serviceProvider),
            cancellationToken);

    public async ValueTask<IReadOnlyList<RuntimeMaterializedActivityInput>> MaterializeInputsAsync(
        ExecutableNode node,
        RuntimeInputBindingResolutionContext resolutionContext,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(resolutionContext);

        var inputs = new List<RuntimeMaterializedActivityInput>();

        foreach (var (inputName, binding) in node.InputBindings)
        {
            var type = ResolveInputType(binding, node.ExecutableNodeId, inputName);
            var value = await MaterializeValueAsync(node, inputName, binding, resolutionContext, cancellationToken, type);

            inputs.Add(BuildInput(node.ExecutableNodeId, inputName, type, value));
        }

        return inputs;
    }

    private async ValueTask<object?> MaterializeValueAsync(
        ExecutableNode node,
        string inputName,
        RuntimeInputBinding binding,
        RuntimeInputBindingResolutionContext resolutionContext,
        CancellationToken cancellationToken,
        Type? resolvedType = null)
    {
        var type = resolvedType ?? ResolveInputType(binding, node.ExecutableNodeId, inputName);
        var resolved = _inputBindingResolver.Resolve(binding, resolutionContext);

        if (resolved.Source == RuntimeInputBindingSource.Expression)
            return CoerceToType(await EvaluateExpressionAsync(resolved, type, binding.TargetType, node.ExecutableNodeId, inputName, resolutionContext, cancellationToken), type);

        if (resolved.Value.HasValue)
            return JsonSerializer.Deserialize(resolved.Value.Value.GetRawText(), type);

        throw new InvalidOperationException($"Input '{inputName}' on executable node '{node.ExecutableNodeId}' is not a supported materialized value binding.");
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
            return materialized is null
                ? ValueEnvelope.Null(binding.TargetType, binding.EffectivePolicy)
                : ValueEnvelope.Inline(
                    binding.TargetType,
                    JsonSerializer.SerializeToElement(materialized, materialized.GetType()),
                    binding.EffectivePolicy);
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

        return new ValueEnvelope(
            binding.TargetType,
            source.Presence,
            source.InlineValue,
            source.ExternalReference,
            binding.EffectivePolicy);
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
                source.ExternalReference!.StorageProfile,
                binding.TargetType,
                payload.Clone(),
                binding.EffectivePolicy),
            cancellationToken);
        return ValueEnvelope.External(binding.TargetType, reference, binding.EffectivePolicy);
    }

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

        var contractMinimum = new ValueProtectionPolicy(
            ToLifecycle(input.Policy.Lifecycle),
            ToStorage(input.Policy.Storage),
            input.Policy.IsSensitive,
            input.Policy.RequiresEncryption,
            input.Policy.RedactionMode,
            input.Policy.RetentionPolicy);
        if (!policy.Satisfies(contractMinimum))
            throw new InvalidOperationException($"VF-ACT-005: Input '{input.Key}' on executable node '{nodeId}' would downgrade its contract protection policy.");
    }

    private static DurableValueLifecycle ToLifecycle(ActivityValueLifecycle lifecycle) => lifecycle switch
    {
        ActivityValueLifecycle.Instance => DurableValueLifecycle.Instance,
        ActivityValueLifecycle.Result => DurableValueLifecycle.Result,
        ActivityValueLifecycle.Audit => DurableValueLifecycle.Audit,
        ActivityValueLifecycle.Custom => DurableValueLifecycle.Custom,
        _ => throw new ArgumentOutOfRangeException(nameof(lifecycle), lifecycle, null)
    };

    private static DurableValueStorage ToStorage(ActivityValueStorage storage) => storage switch
    {
        ActivityValueStorage.Inline => DurableValueStorage.Inline,
        ActivityValueStorage.External => DurableValueStorage.External,
        ActivityValueStorage.Custom => DurableValueStorage.Custom,
        _ => throw new ArgumentOutOfRangeException(nameof(storage), storage, null)
    };

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

        var serviceProvider = resolutionContext.ServiceProvider
            ?? throw new InvalidOperationException($"Input '{inputName}' on executable node '{nodeId}' uses a '{expression.Language}' expression, but no service provider was supplied to evaluate it.");

        if (!StringComparer.Ordinal.Equals(expression.CapabilityProfile, ExpressionCapabilityProfiles.LegacyAmbientV1))
        {
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

        var evaluator = serviceProvider.GetService(typeof(IExpressionEvaluator)) as IExpressionEvaluator
            ?? throw new InvalidOperationException($"Input '{inputName}' on executable node '{nodeId}' uses a '{expression.Language}' expression, but no '{nameof(IExpressionEvaluator)}' is registered.");
        var executionContext = new MaterializationExpressionExecutionContext(resolutionContext, serviceProvider, cancellationToken);
        var expressionValue = BuildExpressionValue(expression);

        try
        {
            return await evaluator.EvaluateAsync(new RuntimeExpression(expression.Language, expressionValue), type, executionContext);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException($"Input '{inputName}' on executable node '{nodeId}' failed to evaluate its '{expression.Language}' expression.", exception);
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
    /// Produces the value handed to the registered expression handler. A <c>Variable</c> expression's
    /// text is a JSON-encoded structured <see cref="VariableReference"/> (reference key plus optional
    /// declaring scope); it is parsed back into a <see cref="JsonElement"/> so the variable handler's
    /// <see cref="VariableReference.TryParse"/> recovers the declaring scope rather than treating the
    /// whole JSON blob as a bare reference key. Other languages pass their raw source text.
    /// </summary>
    private static object? BuildExpressionValue(RuntimeExpressionBinding expression)
    {
        if (!string.Equals(expression.Language, WellKnownExpressionDescriptorTypes.Variable, StringComparison.Ordinal))
            return expression.Expression;

        try
        {
            return JsonSerializer.Deserialize<JsonElement>(expression.Expression);
        }
        catch (JsonException)
        {
            return expression.Expression;
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

    private static RuntimeMaterializedActivityInput BuildInput(string nodeId, string inputName, Type type, object? value)
    {
        var memoryReference = new LiteralMemoryBlockReference($"{nodeId}:{inputName}");
        var argumentType = typeof(InputArgument<>).MakeGenericType(type);
        var argument = (InputArgument)Activator.CreateInstance(argumentType, memoryReference)!;
        return new RuntimeMaterializedActivityInput(inputName, argument, value);
    }

    private Type ResolveInputType(RuntimeInputBinding binding, string nodeId, string inputName)
    {
        if (binding.TargetType.Alias != "Elsa.Any")
        {
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

        if (!binding.Metadata.TryGetValue(InputTypeMetadataKey, out var typeName))
            throw new InvalidOperationException($"Input '{inputName}' on executable node '{nodeId}' is missing '{InputTypeMetadataKey}' metadata.");

        return ResolveType(typeName, nodeId, inputName);
    }

    private static Type ResolveType(string typeName, string nodeId, string inputName)
    {
        var type = Type.GetType(typeName, throwOnError: false);
        if (type is not null)
            return type;

        var delimiterIndex = typeName.IndexOf(',', StringComparison.Ordinal);
        var fullName = delimiterIndex >= 0 ? typeName[..delimiterIndex].Trim() : typeName.Trim();
        var assemblyName = delimiterIndex >= 0 ? typeName[(delimiterIndex + 1)..].Split(',')[0].Trim() : null;

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (!string.IsNullOrWhiteSpace(assemblyName) && !StringComparer.Ordinal.Equals(assembly.GetName().Name, assemblyName))
                continue;

            type = assembly.GetType(fullName, throwOnError: false);
            if (type is not null)
                return type;
        }

        throw new InvalidOperationException($"Input '{inputName}' on executable node '{nodeId}' declares type '{typeName}', but that type could not be loaded.");
    }

    private sealed class RuntimeExpression(string type, object? value) : IExpression
    {
        public string Type { get; set; } = type;
        public object? Value { get; set; } = value;

        public TValue GetValue<TValue>() => (TValue)Value!;
    }

    private sealed class LiteralMemoryBlockReference(string id) : IMemoryBlockReference
    {
        public string Id { get; set; } = id;

        public IMemoryBlock Declare() => new LiteralMemoryBlock();

        public T? Get<T>(IMemoryRegister memoryRegister, IExpressionExecutionContext context)
        {
            var value = GetValue(memoryRegister);
            var objectConverter = context.GetRequiredService<IObjectConverter>();
            return objectConverter.ConvertTo<T>(value);
        }

        public object? Get(IExpressionExecutionContext context) => context.Get(this);

        public T? Get<T>(IExpressionExecutionContext context) => context.Get<T>(this);

        private object? GetValue(IMemoryRegister memoryRegister)
        {
            if (!memoryRegister.Blocks.TryGetValue(Id, out var block))
                block = memoryRegister.Declare(this);

            return block.Value;
        }
    }

    private sealed class LiteralMemoryBlock : IMemoryBlock
    {
        public object? Value { get; set; }
        public object? Metadata { get; set; }
    }

    private sealed class EmptyRuntimeActivityOutputReader : IRuntimeActivityOutputReader
    {
        public static readonly EmptyRuntimeActivityOutputReader Instance = new();

        public bool TryGet(ActiveActivityOutputKey key, out ActiveActivityOutput output)
        {
            output = null!;
            return false;
        }

        public IReadOnlyCollection<ActiveActivityOutput> GetActivityOutputs(string workflowExecutionId, string activityExecutionId) => [];
    }

    /// <summary>
    /// <see cref="IExpressionExecutionContext"/> used to evaluate input expressions before the activity
    /// (and therefore its real execution context) exists. It exposes the request-scoped service provider the
    /// expression handlers need, plus the workflow variables, workflow inputs, and prior activity outputs
    /// carried by the <see cref="RuntimeInputBindingResolutionContext"/>, so that references such as
    /// <c>variables.foo</c>, <c>input.bar</c>, and prior-output accessors resolve. It also implements
    /// <see cref="IMaterializationExpressionState"/> so language-specific pre-processors can surface those
    /// values without a live workflow execution context.
    /// </summary>
    private sealed class MaterializationExpressionExecutionContext(
        RuntimeInputBindingResolutionContext resolutionContext,
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken)
        : IExpressionExecutionContext, IMaterializationExpressionState, IScopedVariableProvider
    {
        public IReadOnlyDictionary<string, object?> WorkflowVariables => resolutionContext.WorkflowVariables;
        public IReadOnlyDictionary<string, object?> WorkflowInputs => resolutionContext.WorkflowInputs;
        public IReadOnlyDictionary<string, object?> ActivityOutputValues => resolutionContext.ActivityOutputValues;

        public IMemoryRegister Memory => null!;
        public IExpressionExecutionContext? ParentContext { get => null; set { } }
        public CancellationToken CancellationToken { get; } = cancellationToken;

        public object? GetRequiredService(Type type) => serviceProvider.GetService(type)
            ?? throw new InvalidOperationException($"Required service '{type.FullName}' is not registered.");

        public bool IsContainedWithinCompositeActivity() => false;
        // Activity inputs are composite-activity scoped and do not exist at materialization time; only workflow inputs are available.
        public bool TryGetActivityInput(string key, out object? value) { value = null; return false; }
        public bool TryGetWorkflowInput(string key, out object? value) => WorkflowInputs.TryGetValue(key, out value);
        public object? GetVariableValueOrDefault(string variableName) => WorkflowVariables.GetValueOrDefault(variableName);
        public string GetCorrelationId() => string.Empty;
        public string GetWorkflowDefinitionId() => string.Empty;
        public string GetWorkflowDefinitionVersionId() => string.Empty;
        public int GetWorkflowDefinitionVersion() => 0;
        public string GetWorkflowInstanceId() => resolutionContext.WorkflowExecutionId;

        public IMemoryBlock GetBlock(IMemoryBlockReference blockReference) => blockReference.Declare();
        public bool TryGetBlock(IMemoryBlockReference blockReference, out IMemoryBlock block) { block = null!; return false; }
        public T? Get<T>(IMemoryBlockReference blockReference) => (T?)blockReference.Declare().Value;
        public void Set(IMemoryBlockReference blockReference, object? value, Action<IMemoryBlock>? configure = null) { }

        public IVariable? GetVariable(string name, bool localScopeOnly = false) =>
            WorkflowVariables.TryGetValue(name, out var value) ? new MaterializationVariable(name, value) : null;

        public IVariable SetVariable<T>(string name, T? value, Action<IMemoryBlock>? configure = null) => new MaterializationVariable(name, value);

        public IEnumerable<IVariable> EnumerateVariablesInScope() =>
            WorkflowVariables.Select(entry => new MaterializationVariable(entry.Key, entry.Value)).ToArray();

        // IScopedVariableProvider — resolves structured/name-based variable access through the visible
        // scope chain threaded from the runtime (workflow + ancestor container scopes, ADR 0027). When
        // no scope chain is present these return false/empty so the variable handler falls back to its
        // workflow-scoped behaviour.
        public bool TryGetScopedVariableValue(VariableReference reference, out object? value)
        {
            if (resolutionContext.VariableScope is { } scope && scope.TryGetValue(reference, out value))
                return true;

            value = null;
            return false;
        }

        public bool TrySetScopedVariableValue(VariableReference reference, object? value) =>
            resolutionContext.VariableScope?.TrySetValue(reference, value) ?? false;

        public IReadOnlyCollection<IVariable> GetVisibleVariables() =>
            resolutionContext.VariableScope?.EnumerateVisibleVariables() ?? [];

        public bool TryGetVariableValueByName(string name, out object? value)
        {
            if (resolutionContext.VariableScope is { } scope && scope.TryGetValueByName(name, out value))
                return true;

            value = null;
            return false;
        }

        public bool TrySetVariableValueByName(string name, object? value) =>
            resolutionContext.VariableScope?.TrySetValueByName(name, value) ?? false;
    }

    /// <summary>
    /// Read-only <see cref="IVariable"/> backed by a fixed materialization-time value. Exposes the value
    /// to the standard variable accessors (e.g. <see cref="IExpressionExecutionContext.GetVariableInScope"/>).
    /// </summary>
    private sealed class MaterializationVariable(string name, object? value) : IVariable
    {
        public string Id { get; set; } = $"variable:{name}";
        public string Name { get; set; } = name;
        public object? DefaultValue { get; set; } = value;
        public Type? StorageDriverType { get; set; }

        public IMemoryBlock Declare() => new LiteralMemoryBlock { Value = DefaultValue };

        public T? Get<T>(IMemoryRegister memoryRegister, IExpressionExecutionContext context) => DefaultValue is T typed ? typed : default;

        public object? Get(IExpressionExecutionContext context) => DefaultValue;

        public T? Get<T>(IExpressionExecutionContext context) => DefaultValue is T typed ? typed : default;
    }
}
