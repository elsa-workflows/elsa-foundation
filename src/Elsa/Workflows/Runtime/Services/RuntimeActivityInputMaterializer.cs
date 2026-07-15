using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Expressions.Core.Constants;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Serialization.Core;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Resolvers;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class RuntimeActivityInputMaterializer : IRuntimeActivityInputMaterializer
{
    public const string InputTypeMetadataKey = "typeName";
    private readonly IRuntimeInputBindingResolver _inputBindingResolver;
    private readonly IRuntimeDurableValueStorageDriverRegistry? _storageDrivers;

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
        IRuntimeDurableValueStorageDriverRegistry storageDrivers)
    {
        ArgumentNullException.ThrowIfNull(inputBindingResolver);
        ArgumentNullException.ThrowIfNull(storageDrivers);
        _inputBindingResolver = inputBindingResolver;
        _storageDrivers = storageDrivers;
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
        var retryInputs = RetryInputs(resolutionContext);

        foreach (var (inputName, binding) in node.InputBindings)
        {
            var type = ResolveInputType(binding, node.ExecutableNodeId, inputName);
            if (retryInputs.Remove(inputName, out var retryState))
            {
                var retryValue = await DecodeRetryInputAsync(retryState, cancellationToken);
                inputs.Add(BuildInput(node.ExecutableNodeId, inputName, type, CoerceToType(retryValue, type)));
                continue;
            }
            var resolved = _inputBindingResolver.Resolve(binding, resolutionContext);

            object? value;
            if (resolved.Source == RuntimeInputBindingSource.Expression)
                value = CoerceToType(await EvaluateExpressionAsync(resolved, type, node.ExecutableNodeId, inputName, resolutionContext, cancellationToken), type);
            else if (resolved.Value.HasValue)
                value = JsonSerializer.Deserialize(resolved.Value.Value.GetRawText(), type);
            else
                throw new InvalidOperationException($"Input '{inputName}' on executable node '{node.ExecutableNodeId}' is not a supported materialized value binding.");

            inputs.Add(BuildInput(node.ExecutableNodeId, inputName, type, value));
        }


        foreach (var (inputName, retryState) in retryInputs.OrderBy(item => item.Key, StringComparer.Ordinal))
        {
            var retryValue = await DecodeRetryInputAsync(retryState, cancellationToken);
            inputs.Add(BuildInput(node.ExecutableNodeId, inputName, retryValue?.GetType() ?? typeof(object), retryValue));
        }

        return inputs;
    }

    private static Dictionary<string, DurableValueState> RetryInputs(RuntimeInputBindingResolutionContext context)
    {
        var result = new Dictionary<string, DurableValueState>(StringComparer.Ordinal);
        foreach (var state in context.DurableValuesByValueId.Values)
        {
            if (!StringComparer.Ordinal.Equals(state.SourceActivityExecutionId, context.ActivityExecutionId) ||
                !state.Metadata.ContainsKey(RuntimeMetadataKeys.RetrySourceActivityExecutionId) ||
                !state.Metadata.TryGetValue(RuntimeMetadataKeys.BoundaryInputName, out var inputName))
            {
                continue;
            }

            if (!result.TryAdd(inputName, state))
                throw new InvalidOperationException($"Retry input snapshot contains duplicate input name '{inputName}'.");
        }

        return result;
    }

    private async ValueTask<object?> DecodeRetryInputAsync(DurableValueState state, CancellationToken cancellationToken)
    {
        var driverKey = state.Type.Id;
        if (string.IsNullOrWhiteSpace(driverKey))
            throw new InvalidOperationException($"Retry input durable value '{state.DurableValueId}' has no storage-driver key.");
        if (_storageDrivers is null)
            throw new InvalidOperationException("Retry input materialization requires the durable-value storage-driver registry.");
        return await _storageDrivers.GetRequired(driverKey).DecodeAsync(state, cancellationToken);
    }

    private static async ValueTask<object?> EvaluateExpressionAsync(
        RuntimeResolvedInput resolved,
        Type type,
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

    private static Type ResolveInputType(RuntimeInputBinding binding, string nodeId, string inputName)
    {
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
