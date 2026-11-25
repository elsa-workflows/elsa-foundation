using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Workflows.Design.Api.Models;
using Elsa.Workflows.Design.Core.Models;
using Xunit;

namespace Elsa.Workflows.Design.Api.Tests;

public sealed class ArgumentDocumentJsonIntegrityTests
{
    [Fact]
    public void The_actual_API_context_round_trips_argument_data_and_variable_defaults_in_the_full_document()
    {
        const string raw = """{ "OrderID":9007199254740993, "amount":1.00, "nil":null, "text":"\u0061 <+>" }""";
        ArgumentValue argument;
        using (var document = JsonDocument.Parse(raw)) argument = new(document.RootElement, "Literal");
        var view = new WorkflowDefinitionStateView(
            [new("variable", "Variable", new TypeReference("Object"), null, ArgumentValue.FromClr(new ScopedDefault { Name = "René <+>" }, "Literal"))],
            new ActivityNode("root", "activity", [new("input", argument, null, null, null, null)], []), [], []);
        var contextType = typeof(WorkflowDefinitionStateView).Assembly.GetType("Elsa.Workflows.Design.Api.WorkflowsDesignJsonContext", throwOnError: true)!;
        var context = (JsonSerializerContext)contextType.GetProperty("WireContext", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        var info = context.GetTypeInfo(typeof(WorkflowDefinitionStateView))!;
        var wire = JsonSerializer.Serialize(view, info);
        var restored = (WorkflowDefinitionStateView)JsonSerializer.Deserialize(wire, info)!;

        var data = Assert.Single(restored.RootActivity!.Inputs).Value.Value!.Value;
        Assert.Equal(raw, data.GetRawText());
        Assert.Equal(9007199254740993L, data.GetProperty("OrderID").GetInt64());
        Assert.Equal(JsonValueKind.Null, data.GetProperty("nil").ValueKind);
        Assert.False(data.TryGetProperty("missing", out _));
        var value = Assert.Single(restored.Variables!).Default!.Value!.Value;
        Assert.Equal("René <+>", value.GetProperty("declared_name").GetString());
        Assert.False(value.TryGetProperty("name", out _));
        Assert.Equal("René <+>", JsonConversionContract.Deserialize<ScopedDefault>(value)!.Name);
    }

    [JsonConversion(MemberMappings = ["declared_name=Name"])]
    public sealed class ScopedDefault
    {
        public string Name { get; set; } = string.Empty;
    }
}
