using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Serialization.Testing;
using Elsa.Workflows.Design.Core.Models;
using Elsa3.Mapping.Conversion;
using Elsa3.Mapping.Resolution;
using Elsa3.Mapping.Tests.Support;
using Elsa3.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using static Elsa3.Mapping.Tests.Elsa3BuiltInConversionTests;
using WorkflowArgumentState = Elsa.Workflows.Design.Core.Models.ArgumentState;

namespace Elsa3.Mapping.Tests;

public sealed class Elsa3NativeStructureRawDataTests
{
    private const string RawData = "{ \"OwnerKey\":\"Ren\\u00e9<+>\", \"N\":1.0, \"x\":null, \"rows\":[ 1e0, null ] }";

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public async Task Converted_native_sequences_preserve_nested_unknown_literal_data_and_scoped_defaults(int depth)
    {
        var originalLiteral = "{\"ORDER_ID\":\"one\",\"extra\":" + RawData + "}";
        var typed = Parse("{\"typeName\":\"" + typeof(JsonImportOrder).FullName +
            "\",\"expression\":{\"type\":\"Object\",\"value\":" + originalLiteral + "}}");
        var leaf = Activity("leaf", "Acme.JsonOrder", ("order", typed));
        var innermost = Sequence("container-0", leaf);
        innermost.AdditionalProperties["variables"] = JsonSerializer.SerializeToElement(new[]
        {
            new Elsa3Variable { Id = "scoped-data", Name = "ScopedData", TypeName = "ObjectDictionary", Value = RawData }
        }, JsonBaseline.Options);
        var root = innermost;
        for (var index = 1; index < depth; index++) root = Sequence("container-" + index, root);
        var definition = ConversionServices.Definition(root);

        await using var provider = ConversionServices.Create(RealActivityCatalog.CreateWith(typeof(JsonOrderImportActivity).Assembly));
        var registry = provider.GetRequiredService<IWellKnownTypeRegistry>();
        foreach (var type in new[] { typeof(JsonImportOrder), typeof(JsonOrderImportActivity) })
            registry.RegisterType(type, TypeAliasConvention.CanonicalAlias(type));
        await using var scope = provider.CreateAsyncScope();
        var converted = await scope.ServiceProvider.GetRequiredService<IElsa3DefinitionConverter>().ConvertAsync(new(definition));
        Assert.True(converted.Succeeded, Describe(converted.Diagnostics));
        Assert.Contains(converted.Diagnostics, diagnostic => diagnostic.Code == Elsa3ActivityDiagnosticCodes.LiteralKeyRewritten);
        AssertNativeData(converted.Value!.State);

        var serializer = SerializationServices.PayloadSerializer();
        AssertNativeData(serializer.Deserialize<WorkflowDefinitionState>(serializer.Serialize(converted.Value.State)));
        Assert.Equal(originalLiteral, leaf.AdditionalProperties["order"].GetProperty("expression").GetProperty("value").GetRawText());
        Assert.Equal(RawData, innermost.AdditionalProperties["variables"][0].GetProperty("value").GetString());

        void AssertNativeData(WorkflowDefinitionState state)
        {
            var payload = state.RootActivity!.Structure!.Payload;
            for (var index = depth - 1; index > 0; index--)
                payload = payload.GetProperty("activities")[0].GetProperty("structure").GetProperty("payload");
            var argument = payload.GetProperty("activities")[0].GetProperty("inputs")[0].GetProperty("value").GetProperty("value");
            Assert.Equal("one", argument.GetProperty("order_id").GetString());
            Assert.Equal(RawData, argument.GetProperty("extra").GetRawText());
            var variable = Assert.Single(payload.GetProperty("variables").EnumerateArray());
            Assert.Equal("scoped-data", variable.GetProperty("referenceKey").GetString());
            Assert.Equal(RawData, variable.GetProperty("default").GetProperty("value").GetRawText());
        }
    }

    [Fact]
    public void Collection_conditions_children_and_payload_fields_keep_raw_tokens_and_native_slot_semantics()
    {
        var source = Activity("native", "Test.Native");
        var condition = new ExpressionDefinition("JavaScript", "args.p.x === null", new TypeReference("Boolean"),
            new Dictionary<string, ExpressionParameterBinding> { ["p"] = new LiteralExpressionParameterBinding(Parse(RawData)) },
            Parse(RawData), ExpressionCapabilityProfiles.BindingReadonlyV1);
        source.AdditionalProperties["case-condition"] = Elsa3TypedValues.WithExpression(Parse("{\"typeName\":\"Boolean\"}"), "JavaScript",
            JsonSerializer.SerializeToElement(condition, JsonBaseline.Options));
        var childSource = Activity("leaf", "Test.Leaf");
        var missing = Activity("unmapped", "Test.Leaf");
        var child = new ActivityNode("leaf", "leaf-v1",
            [new WorkflowArgumentState("data", new ArgumentValue(Parse(RawData), "Literal"), null, null, null, null,
                new AuthoredValueConversionRequest(AuthoredValueConversionMode.Json, Options: Parse(RawData)))], [])
            { ExecutionPlacement = ActivityExecutionPlacement.Background };
        var slots = new ActivityChildSlotDesignDescriptor[]
        {
            new("Case", "unused", "Cases", "Many", CollectionProperty: "cases", ChildProperty: "activity", LabelProperty: "match")
                { ItemIdProperty = "caseId", ConditionProperty = "condition" },
            new("Many", "activities", "Activities", "Many"),
            new("Single", "body", "Body", "One"),
            new("Empty", "otherwise", "Otherwise", "One")
        };
        var plan = new Elsa3NodePlan(source, "$", null)
        {
            Resolved = new(Elsa3ResolvedSource.BuiltInMap, null, "native-v1", "Test.Native", [], [],
                new("test.native", "1", new("test", true, slots, new Dictionary<string, object?>
                {
                    ["seed"] = Parse(RawData), ["mode"] = AuthoredValueConversionMode.Auto, ["unset"] = null
                })))
        };
        plan.Slots.AddRange([
            new("Case", "Selected<+>", childSource, "case-1", "case-condition"), new("Case", null, missing),
            new("Many", null, childSource), new("Many", null, missing), new("Many", null, null),
            new("Single", null, childSource)]);
        plan.Variables.Add(new("scoped-data", "ScopedData", new TypeReference("Any"), null, new(Parse(RawData), "Literal")));
        plan.StructureFields["extra"] = Parse(RawData);
        plan.StructureFields["mode"] = AuthoredValueConversionMode.Xml;
        var mapped = new Dictionary<Elsa3Activity, ActivityNode> { [childSource] = child };

        var structure = Elsa3NativeStructureBuilder.Build(plan, mapped)!;
        Assert.Equal("test.native", structure.Kind);
        Assert.Equal("1", structure.SchemaVersion);
        var payload = structure.Payload;
        Assert.Equal(RawData, payload.GetProperty("seed").GetRawText());
        Assert.Equal(RawData, payload.GetProperty("extra").GetRawText());
        Assert.Equal("xml", payload.GetProperty("mode").GetString());
        Assert.Equal(JsonValueKind.Null, payload.GetProperty("unset").ValueKind);
        Assert.Equal(JsonValueKind.Null, payload.GetProperty("otherwise").ValueKind);
        Assert.Equal(RawData, payload.GetProperty("variables")[0].GetProperty("default").GetProperty("value").GetRawText());
        var cases = payload.GetProperty("cases");
        Assert.Equal(2, cases.GetArrayLength());
        Assert.Equal("Selected<+>", cases[0].GetProperty("match").GetString());
        Assert.Equal("case-1", cases[0].GetProperty("caseId").GetString());
        Assert.Equal(JsonValueKind.Null, cases[1].GetProperty("match").ValueKind);
        Assert.Equal(JsonValueKind.Null, cases[1].GetProperty("activity").ValueKind);
        Assert.False(cases[1].TryGetProperty("condition", out _));
        var retainedCondition = cases[0].GetProperty("condition").GetProperty("value").GetProperty("value");
        Assert.Equal(RawData, retainedCondition.GetProperty("options").GetRawText());
        Assert.Equal(RawData, retainedCondition.GetProperty("parameters").GetProperty("p").GetProperty("value").GetRawText());
        Assert.Equal("Literal", retainedCondition.GetProperty("parameters").GetProperty("p").GetProperty("kind").GetString());
        var many = payload.GetProperty("activities");
        Assert.Single(many.EnumerateArray()); // The old many-slot policy omits missing/null children.
        AssertChild(many[0]);
        AssertChild(payload.GetProperty("body"));
        AssertChild(cases[0].GetProperty("activity"));

        void AssertChild(JsonElement node)
        {
            Assert.Equal("background", node.GetProperty("executionPlacement").GetString());
            var argument = node.GetProperty("inputs")[0];
            Assert.Equal(RawData, argument.GetProperty("value").GetProperty("value").GetRawText());
            Assert.Equal("json", argument.GetProperty("conversion").GetProperty("mode").GetString());
            Assert.Equal(RawData, argument.GetProperty("conversion").GetProperty("options").GetRawText());
        }
    }

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
