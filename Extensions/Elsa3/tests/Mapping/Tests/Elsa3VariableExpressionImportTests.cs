using System.Text.Json;
using Elsa3.Models;
using Xunit;
using static Elsa3.Mapping.Tests.Elsa3MemoryReferenceImportFixture;

namespace Elsa3.Mapping.Tests;

public sealed class Elsa3VariableExpressionImportTests
{
    [Fact]
    public async Task A_variable_expression_reads_the_declared_workflow_variable_by_id()
    {
        var definition = Workflow("variable-expression", Consumer("read", "variable-id", "stale-name"),
            variables: [Variable("variable-id", "actual-name")]);

        var result = await Fixture().ImportAsync(definition, "variable-expression.json");

        Assert.True(result.Succeeded, Diagnostics(result));
        var value = ReadValue(FindInput(result.Value!, "read", "key:value"));
        Assert.Equal("Variable", value.ExpressionType);
        Assert.Equal("variable-id", GetProperty(value.Value, "referenceKey").GetString());
        Assert.Equal("workflow", GetProperty(value.Value, "declaringScopeId").GetString());
        Assert.DoesNotContain("stale-name", SerializeState(result.Value!), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_variable_expression_reads_a_container_variable_from_its_declaring_scope()
    {
        var definition = Workflow("scoped-variable-expression", Container("scope", [Variable("local", "local")], Consumer("read", "local")));

        var result = await Fixture().ImportAsync(definition, "scoped-variable-expression.json");

        Assert.True(result.Succeeded, Diagnostics(result));
        var value = ReadValue(FindInput(result.Value!, "read", "key:value"));
        Assert.Equal("Variable", value.ExpressionType);
        Assert.Equal("local", GetProperty(value.Value, "referenceKey").GetString());
        Assert.Equal("scope", GetProperty(value.Value, "declaringScopeId").GetString());
    }

    [Fact]
    public async Task The_nearest_enclosing_declaration_takes_precedence_over_outer_scopes()
    {
        var inner = Container("inner", [Variable("shared-id", "inner")], Consumer("read", "shared-id"));
        var definition = Workflow("nearest-variable-expression", Container("outer", [Variable("shared-id", "outer")], inner),
            variables: [Variable("shared-id", "workflow")]);

        var result = await Fixture().ImportAsync(definition, "nearest-variable-expression.json");

        Assert.True(result.Succeeded, Diagnostics(result));
        var value = ReadValue(FindInput(result.Value!, "read", "key:value"));
        Assert.Equal("inner", GetProperty(value.Value, "declaringScopeId").GetString());
    }

    [Fact]
    public async Task Duplicate_ids_in_the_same_container_are_refused()
    {
        var definition = Workflow("duplicate-container-variable", Container("scope",
            [Variable("shared-id", "one"), Variable("shared-id", "two")], Consumer("read", "shared-id")));

        var result = await Fixture().ImportAsync(definition, "duplicate-container-variable.json");

        Assert.False(result.Succeeded);
        Assert.Null(result.Value);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "VF-IMP-002");
    }

    [Fact]
    public async Task A_variable_expression_does_not_read_a_variable_from_a_sibling_scope()
    {
        var definition = Workflow("sibling-variable-expression", Activity("root", "Elsa.Sequence", children:
        [
            Container("left", [Variable("local", "local")]),
            Container("right", [], Consumer("read", "local"))
        ]));

        var result = await Fixture().ImportAsync(definition, "sibling-variable-expression.json");

        Assert.False(result.Succeeded);
        Assert.Null(result.Value);
        var diagnostic = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == "VF-IMP-003");
        Assert.Equal("$.root.activities[1].activities[0].Value.expression.value.id", diagnostic.Path);
        Assert.False(string.IsNullOrWhiteSpace(diagnostic.Guidance));
    }

    [Theory]
    [InlineData("missing", "VF-IMP-001")]
    [InlineData("duplicate", "VF-IMP-002")]
    [InlineData("blank", "VF-IMP-004")]
    public async Task An_unresolvable_variable_expression_is_refused_during_import(string kind, string code)
    {
        var referencedId = kind == "blank" ? " " : "variable-id";
        var declarations = kind == "duplicate"
            ? new[] { Variable("variable-id", "one"), Variable("variable-id", "two") }
            : new[] { Variable("other-id", "same-name") };
        var definition = Workflow(kind, Consumer("read", referencedId, "same-name"), variables: declarations);

        var result = await Fixture().ImportAsync(definition, $"{kind}.json");

        Assert.False(result.Succeeded);
        Assert.Null(result.Value);
        var diagnostic = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == code);
        Assert.Equal("$.root.Value.expression.value.id", diagnostic.Path);
        Assert.Equal("read", diagnostic.Metadata["ActivityNodeId"]);
        Assert.False(string.IsNullOrWhiteSpace(diagnostic.Guidance));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("\"variable-id\"")]
    [InlineData("{}")]
    [InlineData("{\"id\":42}")]
    public async Task A_malformed_variable_declaration_is_refused(string json)
    {
        var consumer = Consumer("read", "variable-id");
        consumer.AdditionalProperties["Value"] = JsonSerializer.SerializeToElement(new
        {
            typeName = "String",
            expression = new { type = "Variable", value = JsonSerializer.Deserialize<JsonElement>(json) }
        });
        var definition = Workflow("malformed-variable-expression", consumer, variables: [Variable("variable-id", "variable")]);

        var result = await Fixture().ImportAsync(definition, "malformed-variable-expression.json");

        Assert.False(result.Succeeded);
        Assert.Null(result.Value);
        Assert.Contains(result.Diagnostics, diagnostic => diagnostic.Code == "VF-IMP-004");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task A_variable_holding_the_loop_value_is_read_only_inside_that_loop(bool insideLoop)
    {
        var consumer = Consumer("read", "captured");
        var loop = Activity("loop", "Elsa.ForEach", new Dictionary<string, JsonElement>
        {
            ["CurrentValue"] = MemoryReference("captured"),
            ["Body"] = JsonSerializer.SerializeToElement(insideLoop ? consumer : Activity("empty", "Elsa.Sequence"), new JsonSerializerOptions(JsonSerializerDefaults.Web))
        });
        var root = insideLoop ? loop : Activity("root", "Elsa.Sequence", children: [loop, consumer]);
        var definition = Workflow("loop-variable-expression", root, variables: [Variable("captured", "captured")]);

        var result = await Fixture().ImportAsync(definition, "loop-variable-expression.json");

        if (insideLoop)
        {
            Assert.True(result.Succeeded, Diagnostics(result));
            var value = ReadValue(FindInput(result.Value!, "read", "key:value"));
            Assert.Equal("loop", GetProperty(value.Value, "declaringScopeId").GetString());
            Assert.Equal("currentItem", GetProperty(value.Value, "referenceKey").GetString());
        }
        else
        {
            Assert.False(result.Succeeded);
            Assert.Null(result.Value);
            var diagnostic = Assert.Single(result.Diagnostics, diagnostic => diagnostic.Code == Elsa3ActivityDiagnosticCodes.OutputNotRepresentable);
            Assert.EndsWith(".Value.expression.value.id", diagnostic.Path, StringComparison.Ordinal);
        }
    }

    private static Elsa3MemoryReferenceImportFixture Fixture() => new(ActivityShape.Create("Elsa.Consumer", inputs: ["Value"]));

    private static Elsa3Variable Variable(string id, string name) => new() { Id = id, Name = name, TypeName = "String", Value = "hello" };

    private static Elsa3Activity Consumer(string nodeId, string variableId, string name = "local") => Activity(nodeId, "Elsa.Consumer",
        new Dictionary<string, JsonElement>
        {
            ["Value"] = JsonSerializer.SerializeToElement(new
            {
                typeName = "String",
                expression = new { type = "Variable", value = new { id = variableId, name, typeName = "String" } }
            })
        });

    private static Elsa3Activity Container(string nodeId, Elsa3Variable[] variables, params Elsa3Activity[] children) =>
        Activity(nodeId, "Elsa.Sequence", new Dictionary<string, JsonElement>
        {
            ["variables"] = JsonSerializer.SerializeToElement(variables, new JsonSerializerOptions(JsonSerializerDefaults.Web))
        }, children);

    private static string Diagnostics<T>(Elsa3MigrationResult<T> result) where T : class => string.Join(Environment.NewLine,
        result.Diagnostics.Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));
}
