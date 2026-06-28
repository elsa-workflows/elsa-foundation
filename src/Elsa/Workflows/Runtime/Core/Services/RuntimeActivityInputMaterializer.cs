using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Expressions.Core.Contracts;
using Elsa.Serialization.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Resolvers;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class RuntimeActivityInputMaterializer : IRuntimeActivityInputMaterializer
{
    public const string InputTypeMetadataKey = "typeName";
    private readonly IRuntimeInputBindingResolver _inputBindingResolver;

    public RuntimeActivityInputMaterializer()
        : this(new RuntimeInputBindingResolver())
    {
    }

    public RuntimeActivityInputMaterializer(IRuntimeInputBindingResolver inputBindingResolver)
    {
        ArgumentNullException.ThrowIfNull(inputBindingResolver);

        _inputBindingResolver = inputBindingResolver;
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
            var resolved = _inputBindingResolver.Resolve(binding, resolutionContext);

            object? value;
            if (resolved.Source == RuntimeInputBindingSource.Expression)
                value = await EvaluateExpressionAsync(resolved, type, node.ExecutableNodeId, inputName, resolutionContext, cancellationToken);
            else if (resolved.Value.HasValue)
                value = JsonSerializer.Deserialize(resolved.Value.Value.GetRawText(), type);
            else
                throw new InvalidOperationException($"Input '{inputName}' on executable node '{node.ExecutableNodeId}' is not a supported materialized value binding.");

            inputs.Add(BuildInput(node.ExecutableNodeId, inputName, type, value));
        }

        return inputs;
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

        var serviceProvider = resolutionContext.ServiceProvider
            ?? throw new InvalidOperationException($"Input '{inputName}' on executable node '{nodeId}' uses a '{expression.Language}' expression, but no service provider was supplied to evaluate it.");

        var evaluator = serviceProvider.GetService(typeof(IExpressionEvaluator)) as IExpressionEvaluator
            ?? throw new InvalidOperationException($"Input '{inputName}' on executable node '{nodeId}' uses a '{expression.Language}' expression, but no '{nameof(IExpressionEvaluator)}' is registered.");
        var executionContext = new MaterializationExpressionExecutionContext(serviceProvider, cancellationToken);

        try
        {
            return await evaluator.EvaluateAsync(new RuntimeExpression(expression.Language, expression.Expression), type, executionContext);
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
    /// Minimal <see cref="IExpressionExecutionContext"/> used to evaluate input expressions before the
    /// activity (and therefore its real execution context) exists. It exposes the request-scoped service
    /// provider the expression handlers need and reports no variables, inputs, or memory.
    /// </summary>
    private sealed class MaterializationExpressionExecutionContext(IServiceProvider serviceProvider, CancellationToken cancellationToken)
        : IExpressionExecutionContext
    {
        public IMemoryRegister Memory => null!;
        public IExpressionExecutionContext? ParentContext { get => null; set { } }
        public CancellationToken CancellationToken { get; } = cancellationToken;

        public object? GetRequiredService(Type type) => serviceProvider.GetService(type)
            ?? throw new InvalidOperationException($"Required service '{type.FullName}' is not registered.");

        public bool IsContainedWithinCompositeActivity() => false;
        public bool TryGetActivityInput(string key, out object? value) { value = null; return false; }
        public bool TryGetWorkflowInput(string key, out object? value) { value = null; return false; }
        public object? GetVariableValueOrDefault(string variableName) => null;
        public string GetCorrelationId() => string.Empty;
        public string GetWorkfowDefinitionId() => string.Empty;
        public string GetWorkfowDefinitionVersionId() => string.Empty;
        public int GetWorkfowDefinitionVersion() => 0;
        public string GetWorkfowInstanceId() => string.Empty;

        public IMemoryBlock GetBlock(IMemoryBlockReference blockReference) => blockReference.Declare();
        public bool TryGetBlock(IMemoryBlockReference blockReference, out IMemoryBlock block) { block = null!; return false; }
        public T? Get<T>(IMemoryBlockReference blockReference) => (T?)blockReference.Declare().Value;
        public void Set(IMemoryBlockReference blockReference, object? value, Action<IMemoryBlock>? configure = null) { }

        public IVariable? GetVariable(string name, bool localScopeOnly = false) => null;
        public IVariable SetVariable<T>(string name, T? value, Action<IMemoryBlock>? configure = null) => null!;
        public IEnumerable<IVariable> EnumerateVariablesInScope() => [];
    }
}
