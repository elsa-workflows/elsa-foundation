using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Serialization.Testing;
using Elsa.Workflows.Design.Core.Models;
using Elsa3.Mapping.Tests.Support;
using Elsa3.Models;
using Xunit;
using static Elsa3.Mapping.Tests.Elsa3BuiltInConversionTests;

namespace Elsa3.Mapping.Tests;

public sealed class Elsa3PortableExpressionSerializationTests
{
    private const string RawDefault = "{ \"OwnerKey\":\"Ren\\u00e9\", \"N\":1.0, \"x\":null }";

    [Theory]
    [InlineData("String")]
    [InlineData("Boolean")]
    [InlineData("Any")]
    public async Task Lowered_portable_expressions_keep_exact_type_parameter_and_default_contracts(string alias)
    {
        var (activityType, sourceKey, targetKey, inputName, sourceType, script) = alias switch
        {
            "String" => ("Elsa.WriteLine", "text", "text", "Customer", "String", "getInput('Customer') + getVariable('Greeting')"),
            "Boolean" => ("Elsa.If", "condition", "Condition", "Enabled", "Boolean", "getInput('Enabled')"),
            _ => ("Elsa.ForEach", "items", "Collection", "Items", "Object", "getInput('Items')")
        };
        var definition = ConversionServices.Definition(Sequence("root", Activity("subject", activityType,
            (sourceKey, Expression("JavaScript", script, sourceType)))), variables:
        [
            new Elsa3Variable { Id = "greeting", Name = "Greeting", TypeName = "String", Value = "René<+>" },
            new Elsa3Variable { Id = "data", Name = "RawData", TypeName = "ObjectDictionary", Value = RawDefault }
        ]);
        definition.Inputs.Add(new Elsa3WorkflowArgumentDefinition { Name = inputName, Type = sourceType });
        var converted = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), definition);
        Assert.True(converted.Succeeded, Describe(converted.Diagnostics));
        var state = converted.Value!.State;
        var payload = Assert.IsType<JsonElement>(Input(Node(state, "subject"), targetKey).Value.Value);
        Assert.Equal(alias, payload.GetProperty("resultType").GetProperty("alias").GetString());
        Assert.Equal(nameof(CollectionKind.Single), payload.GetProperty("resultType").GetProperty("collectionKind").GetString());
        Assert.False(payload.GetProperty("resultType").TryGetProperty("Alias", out _));
        var expression = payload.Deserialize<ExpressionDefinition>(JsonBaseline.Options)!;
        Assert.Equal(alias, expression.ResultType.Alias);
        Assert.Equal(inputName, Assert.IsType<WorkflowRequestExpressionParameterBinding>(expression.Parameters[inputName]).MemberKey);
        Assert.DoesNotContain(inputName.ToLowerInvariant(), expression.Parameters.Keys);
        if (alias == "String")
            Assert.Equal("greeting", Assert.IsType<VariableExpressionParameterBinding>(expression.Parameters["variables.Greeting"]).VariableKey);

        var serializer = SerializationServices.PayloadSerializer();
        var restored = serializer.Deserialize<WorkflowDefinitionState>(serializer.Serialize(state));
        var restoredPayload = Assert.IsType<JsonElement>(Input(Node(restored, "subject"), targetKey).Value.Value);
        var restoredExpression = restoredPayload.Deserialize<ExpressionDefinition>(JsonBaseline.Options)!;
        Assert.Equal(alias, restoredExpression.ResultType.Alias);
        Assert.Equal(expression.Parameters.Keys, restoredExpression.Parameters.Keys);
        Assert.Equal(RawDefault, Assert.Single(restored.Variables, variable => variable.ReferenceKey == "data").Default!.Value!.Value.GetRawText());
        Assert.Equal("\"René<+>\"", Assert.Single(restored.Variables, variable => variable.ReferenceKey == "greeting").Default!.Value!.Value.GetRawText());

        var wrongCase = JsonNode.Parse(payload.GetRawText())!;
        var resultType = wrongCase["resultType"]!.AsObject();
        resultType["Alias"] = resultType["alias"]!.DeepClone();
        resultType.Remove("alias");
        var refusedType = JsonSerializer.SerializeToElement(wrongCase, JsonBaseline.Options).Deserialize<ExpressionDefinition>(JsonBaseline.Options)!;
        Assert.Equal(string.Empty, refusedType.ResultType.Alias); // Exact reads do not rescue a malformed producer.
    }

    [Fact]
    public async Task Generated_script_argument_code_keeps_quoted_authored_names_with_the_shared_encoder()
    {
        const string name = "Customer<+>";
        var definition = ConversionServices.Definition(Sequence("root", Activity("script", "Elsa.RunJavaScript",
            ("script", Literal("return getInput('Customer<+>');")))));
        definition.Inputs.Add(new Elsa3WorkflowArgumentDefinition { Name = name, Type = "String" });
        var converted = await ConversionServices.ConvertAsync(RealActivityCatalog.Create(), definition);
        Assert.True(converted.Succeeded, Describe(converted.Diagnostics));
        var payload = Assert.IsType<JsonElement>(Input(Node(converted.Value!.State, "script"), "arguments").Value.Value);
        var expression = payload.Deserialize<ExpressionDefinition>(JsonBaseline.Options)!;
        Assert.Contains("\"Customer<+>\"", expression.Source, StringComparison.Ordinal);
        var value = await PortableJavaScript.EvaluateAsync(expression, new Dictionary<string, JsonElement>
        {
            [name] = JsonSerializer.SerializeToElement("René<+>", JsonBaseline.Options)
        });
        Assert.Equal("René<+>", value.GetProperty(name).GetString());
    }
}
