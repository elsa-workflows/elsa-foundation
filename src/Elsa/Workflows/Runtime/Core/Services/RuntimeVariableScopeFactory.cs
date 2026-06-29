using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// Builds the runtime <see cref="VariableScope"/> chain that makes container-scoped and workflow
/// variables resolvable during input-expression evaluation (ADR 0027). It projects the declared
/// variables a container owns from its compiled executable structure (the generic <c>variables</c>
/// shape both <c>Sequence</c> and <c>Flowchart</c> emit) and assembles an ordered chain — workflow
/// scope outermost, nearest container innermost — keyed per concrete container execution (#210).
/// </summary>
public sealed class RuntimeVariableScopeFactory
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Reads the container-scoped variable declarations carried by an executable node's structure
    /// payload, keyed by reference key. Returns an empty map when the node has no structure, declares
    /// no variables, or is not a container.
    /// </summary>
    public IReadOnlyDictionary<string, IVariable> ProjectDeclaredVariables(ExecutableNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        if (node.Structure is null)
            return EmptyVariables;

        StructureVariablesProjection? projection;
        try
        {
            projection = node.Structure.Payload.Deserialize<StructureVariablesProjection>(SerializerOptions);
        }
        catch (JsonException)
        {
            return EmptyVariables;
        }

        var declarations = projection?.Variables;
        if (declarations is null || declarations.Count == 0)
            return EmptyVariables;

        var result = new Dictionary<string, IVariable>(StringComparer.Ordinal);
        foreach (var declaration in declarations)
        {
            if (string.IsNullOrEmpty(declaration.ReferenceKey))
                continue;

            result[declaration.ReferenceKey] = ToVariable(declaration);
        }

        return result;
    }

    /// <summary>
    /// Projects a container/workflow node's declared variables into the name → default-value snapshot the
    /// runtime seeds as start-time workflow-variable state (Seam C, #254). Reads the same compiled
    /// <c>variables</c> structure shape as <see cref="ProjectDeclaredVariables"/> but keys by the authored
    /// variable name (what <c>variables.*</c> expressions reference) and carries only the declared default.
    /// Returns an empty map when the node declares no variables. Last declaration wins on duplicate names.
    /// </summary>
    public IReadOnlyDictionary<string, object?> ProjectDeclaredVariableDefaultsByName(ExecutableNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        var declared = ProjectDeclaredVariables(node);
        if (declared.Count == 0)
            return EmptyDefaults;

        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var variable in declared.Values)
        {
            if (string.IsNullOrEmpty(variable.Name))
                continue;

            result[variable.Name] = variable.DefaultValue;
        }

        return result;
    }

    /// <summary>
    /// Builds the visible <see cref="VariableScope"/> for the innermost layer, chaining each layer to
    /// its parent. Layers must be ordered outermost-first (workflow scope first, nearest container
    /// last). Returns <c>null</c> when no layers are supplied.
    /// </summary>
    public VariableScope? BuildChain(IReadOnlyList<RuntimeContainerScopeLayer> layersOutermostFirst)
    {
        ArgumentNullException.ThrowIfNull(layersOutermostFirst);

        VariableScope? current = null;
        foreach (var layer in layersOutermostFirst)
        {
            current = new VariableScope(
                layer.ScopeId,
                layer.Variables,
                parent: current,
                executionId: layer.ExecutionId,
                initialValues: layer.Values);

            if (layer.IsCompleted)
                current.Complete();
        }

        return current;
    }

    private static IVariable ToVariable(VariableDefinition definition) =>
        new RuntimeScopedVariable(definition.Name, definition.ReferenceKey, definition.Default?.Value);

    private static readonly IReadOnlyDictionary<string, IVariable> EmptyVariables =
        new Dictionary<string, IVariable>(StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, object?> EmptyDefaults =
        new Dictionary<string, object?>(StringComparer.Ordinal);

    private sealed class StructureVariablesProjection
    {
        [JsonPropertyName("variables")]
        public IReadOnlyCollection<VariableDefinition>? Variables { get; init; }
    }

    /// <summary>
    /// Minimal <see cref="IVariable"/> for a runtime scope declaration. Carries the declaration's
    /// name, reference key (as the memory block id), and default value without depending on the
    /// activity authoring stack.
    /// </summary>
    private sealed class RuntimeScopedVariable(string name, string referenceKey, object? defaultValue) : IVariable
    {
        public string Id { get; set; } = referenceKey;
        public string Name { get; set; } = name;
        public object? DefaultValue { get; set; } = defaultValue;
        public Type? StorageDriverType { get; set; }

        public IMemoryBlock Declare() => new RuntimeScopedVariableBlock(DefaultValue);

        public T? Get<T>(IMemoryRegister memoryRegister, IExpressionExecutionContext context) =>
            DefaultValue is T typed ? typed : default;

        public T? Get<T>(IExpressionExecutionContext context) => DefaultValue is T typed ? typed : default;
    }

    private sealed class RuntimeScopedVariableBlock(object? value) : IMemoryBlock
    {
        public object? Value { get; set; } = value;
        public object? Metadata { get; set; }
    }
}
