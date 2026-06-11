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

    public IReadOnlyList<RuntimeMaterializedActivityInput> MaterializeInputs(ExecutableNode node) =>
        MaterializeInputs(
            node,
            new RuntimeInputBindingResolutionContext(
                workflowExecutionId: "literal-only",
                activityExecutionId: "literal-only",
                durableValuesByValueId: new Dictionary<string, DurableValueState>(),
                activityOutputs: EmptyRuntimeActivityOutputReader.Instance));

    public IReadOnlyList<RuntimeMaterializedActivityInput> MaterializeInputs(
        ExecutableNode node,
        RuntimeInputBindingResolutionContext resolutionContext)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(resolutionContext);

        var inputs = new List<RuntimeMaterializedActivityInput>();

        foreach (var (inputName, binding) in node.InputBindings)
        {
            var resolved = _inputBindingResolver.Resolve(binding, resolutionContext);
            if (!resolved.Value.HasValue)
                throw new InvalidOperationException($"Input '{inputName}' on executable node '{node.ExecutableNodeId}' is not a supported materialized value binding.");

            if (!binding.Metadata.TryGetValue(InputTypeMetadataKey, out var typeName))
                throw new InvalidOperationException($"Input '{inputName}' on executable node '{node.ExecutableNodeId}' is missing '{InputTypeMetadataKey}' metadata.");

            var type = ResolveType(typeName, node.ExecutableNodeId, inputName);
            var memoryReference = new LiteralMemoryBlockReference($"{node.ExecutableNodeId}:{inputName}");
            var argumentType = typeof(InputArgument<>).MakeGenericType(type);
            var argument = (InputArgument)Activator.CreateInstance(argumentType, memoryReference)!;
            var value = JsonSerializer.Deserialize(resolved.Value.Value.GetRawText(), type);
            inputs.Add(new RuntimeMaterializedActivityInput(inputName, argument, value));
        }

        return inputs;
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
}
