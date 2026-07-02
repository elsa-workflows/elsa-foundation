using System.Text.Json;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// Branch coverage for <see cref="RuntimeVariableScopeFactory"/>: projecting declared variables from
/// a container node's compiled structure payload and assembling the visible scope chain (ADR 0027).
/// </summary>
public sealed class RuntimeVariableScopeFactoryTests
{
    private readonly RuntimeVariableScopeFactory _factory = new();

    [Fact]
    public void ProjectDeclaredVariables_returns_empty_when_node_has_no_structure()
    {
        var node = NodeWithStructure(null);

        Assert.Empty(_factory.ProjectDeclaredVariables(node));
    }

    [Fact]
    public void ProjectDeclaredVariables_returns_empty_when_structure_declares_no_variables()
    {
        var node = NodeWithStructure(JsonSerializer.SerializeToElement(new { activities = new[] { "node-a" } }));

        Assert.Empty(_factory.ProjectDeclaredVariables(node));
    }

    [Fact]
    public void ProjectDeclaredVariables_reads_variable_declarations_keyed_by_reference_key()
    {
        var node = NodeWithStructure(StructurePayload(("var-counter", "Counter", "0"), ("var-flag", "Flag", "true")));

        var declared = _factory.ProjectDeclaredVariables(node);

        Assert.Equal(2, declared.Count);
        Assert.Equal("Counter", declared["var-counter"].Name);
        Assert.Equal("0", declared["var-counter"].DefaultValue?.ToString());
        Assert.Equal("Flag", declared["var-flag"].Name);
    }

    [Fact]
    public void ProjectDeclaredVariables_skips_declarations_without_reference_keys()
    {
        var payload = StructurePayload(("", "Nameless", null));

        Assert.Empty(_factory.ProjectDeclaredVariables(NodeWithStructure(payload)));
    }

    [Fact]
    public void ProjectDeclaredVariableDefaultsByName_keys_defaults_by_authored_variable_name()
    {
        var node = NodeWithStructure(StructurePayload(("var-greeting", "greeting", "Hello"), ("var-flag", "flag", "true")));

        var defaults = _factory.ProjectDeclaredVariableDefaultsByName(node);

        Assert.Equal(2, defaults.Count);
        // Defaults round-trip through the compiled structure as JSON, so values surface as JsonElement.
        Assert.Equal("Hello", defaults["greeting"]?.ToString());
        Assert.Equal("true", defaults["flag"]?.ToString());
    }

    [Fact]
    public void ProjectDeclaredVariableDefaultsByName_returns_empty_when_node_declares_no_variables()
    {
        Assert.Empty(_factory.ProjectDeclaredVariableDefaultsByName(NodeWithStructure(null)));
    }

    [Fact]
    public void BuildChain_returns_null_for_no_layers()
    {
        Assert.Null(_factory.BuildChain([]));
    }

    [Fact]
    public void BuildChain_assembles_outermost_first_into_a_visible_parent_chain()
    {
        var outer = new RuntimeContainerScopeLayer("outer", "exec-outer", Vars(("var-o", "Outer", 1)), Values());
        var inner = new RuntimeContainerScopeLayer("inner", "exec-inner", Vars(("var-i", "Inner", 2)), Values(("var-i", 20)));

        var chain = _factory.BuildChain([outer, inner]);

        Assert.NotNull(chain);
        Assert.Equal("inner", chain!.ScopeId);
        Assert.Equal("exec-inner", chain.ExecutionId);
        Assert.Equal("outer", chain.Parent!.ScopeId);

        Assert.True(chain.TryGetValue(new VariableReference("var-i", "inner"), out var innerValue));
        Assert.Equal(20, innerValue);
        Assert.True(chain.TryGetValue(new VariableReference("var-o", "outer"), out var outerValue));
        Assert.Equal(1, outerValue); // falls back to default when no stored value
    }

    [Fact]
    public void BuildChain_marks_completed_layers_as_non_live()
    {
        var completed = new RuntimeContainerScopeLayer("container", "exec-1", Vars(("var-c", "Counter", 1)), Values(("var-c", 7)), IsCompleted: true);

        var chain = _factory.BuildChain([completed]);

        Assert.NotNull(chain);
        Assert.True(chain!.IsCompleted);
        // A completed scope is no longer live: reads and writes are rejected (#210).
        Assert.False(chain.TryGetValue(new VariableReference("var-c", "container"), out _));
        Assert.False(chain.TrySetValue(new VariableReference("var-c", "container"), 99));
    }

    private static ExecutableNode NodeWithStructure(JsonElement? structurePayload) =>
        new(
            executableNodeId: "container",
            authoredActivityId: "authored-container",
            activityType: "test/container",
            activityTypeVersion: "1.0.0",
            descriptorType: "test/descriptor",
            descriptorPayload: JsonSerializer.SerializeToElement(new { }),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>(),
            structure: structurePayload is { } payload
                ? new ExecutableActivityStructure("test.structure", "1.0.0", payload)
                : null);

    private static JsonElement StructurePayload(params (string ReferenceKey, string Name, string? Default)[] variables) =>
        JsonSerializer.SerializeToElement(
            new
            {
                variables = variables.Select(v => new VariableDefinition(
                    ReferenceKey: v.ReferenceKey,
                    Name: v.Name,
                    Type: new Elsa.Primitives.Models.TypeReference("String"),
                    StorageDriverType: null,
                    Default: new ArgumentValue(v.Default, "Literal"))).ToArray()
            },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

    private static IReadOnlyDictionary<string, IVariable> Vars(params (string Key, string Name, object? Value)[] variables) =>
        variables.ToDictionary(
            v => v.Key,
            v => (IVariable)new TestVariable(v.Name, v.Key, v.Value),
            StringComparer.Ordinal);

    private static IReadOnlyDictionary<string, object?> Values(params (string Key, object? Value)[] values) =>
        values.ToDictionary(v => v.Key, v => v.Value, StringComparer.Ordinal);

    private sealed class TestVariable(string name, string id, object? defaultValue) : IVariable
    {
        public string Id { get; set; } = id;
        public string Name { get; set; } = name;
        public object? DefaultValue { get; set; } = defaultValue;
        public Type? StorageDriverType { get; set; }

        public IMemoryBlock Declare() => new TestBlock(DefaultValue);
        public T? Get<T>(IMemoryRegister memoryRegister, IExpressionExecutionContext context) => DefaultValue is T t ? t : default;
        public T? Get<T>(IExpressionExecutionContext context) => DefaultValue is T t ? t : default;

        private sealed class TestBlock(object? value) : IMemoryBlock
        {
            public object? Value { get; set; } = value;
            public object? Metadata { get; set; }
        }
    }
}
