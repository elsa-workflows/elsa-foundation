using System.Text.Json;
using Elsa.Activities.Flowchart.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using FlowchartActivity = Elsa.Activities.Flowchart.Activities.Flowchart;

namespace Elsa.Activities.Flowchart.Tests;

/// <summary>
/// Regression coverage for elsa-foundation#902: a Flowchart-nested activity input whose authored
/// wire JSON carries <c>"conversion": { "mode": "json" }</c> — the camelCase <see cref="AuthoredValueConversionMode"/>
/// string the global FastEndpoints API options emit for <c>ArgumentState.Conversion</c> — must project through
/// the container structure handler. Before the fix the handler deserialized nested payloads with bare
/// <c>JsonSerializerDefaults.Web</c> options that could not read the enum string, so projecting any visually
/// authored container with a conversion request threw <see cref="JsonException"/> at
/// <c>FlowchartStructureHandler.ReadAuthoredStructure</c> during preflight/publish. The authored <c>Json</c> mode
/// is what publication pins to the built-in <c>elsa.json@1</c> conversion plan.
/// </summary>
public sealed class FlowchartStructureConversionTests
{
    private static IActivityStructureService StructureService()
    {
        var services = new ServiceCollection();
        services.AddScoped<IActivityStructureService, DefaultActivityStructureService>();
        new ActivitiesFlowchartFeature().ConfigureServices(services);
        return services.BuildServiceProvider().GetRequiredService<IActivityStructureService>();
    }

    // Authored wire JSON exactly as the FastEndpoints options emit it: camelCase property names and a camelCase
    // AuthoredValueConversionMode string ("json"). This is parsed as a raw payload rather than round-tripped from
    // an in-memory model, because serializing the model with Web options would write the enum as a number and
    // would not reproduce the string-read failure.
    private const string WireJson =
        """
        {
          "activities": [
            {
              "nodeId": "write-one",
              "activityVersionId": "activity-write-line",
              "inputs": [
                {
                  "referenceKey": "Text",
                  "value": { "value": "{\"name\":\"Grace\"}", "expressionType": "Literal" },
                  "conversion": { "mode": "json" }
                }
              ],
              "outputs": []
            }
          ],
          "connections": [],
          "startNodeId": null,
          "variables": []
        }
        """;

    private static ActivityNode FlowchartNodeFromWire() =>
        new(
            "flow",
            "activity-flowchart",
            Inputs: [],
            Outputs: [],
            Structure: new ActivityNodeStructure(
                FlowchartActivity.StructureKind,
                FlowchartActivity.StructureSchemaVersion,
                JsonSerializer.Deserialize<JsonElement>(WireJson)));

    [Fact]
    public void ProjectChildren_ReadsAuthoredCamelCaseConversionEnum_OnContainerNestedInput()
    {
        var service = StructureService();
        var flowchart = FlowchartNodeFromWire();

        var projection = Assert.Single(service.ProjectChildren(flowchart));
        var child = Assert.Single(projection.Activities);
        var input = Assert.Single(child.Inputs);

        Assert.Equal("Text", input.ReferenceKey);
        Assert.NotNull(input.Conversion);
        Assert.Equal(AuthoredValueConversionMode.Json, input.Conversion!.Mode);
    }

    [Fact]
    public void RemapExecutableStructure_RewritesGraphReferencesAndNodeMetadataKeysOnly()
    {
        var service = StructureService();
        var authored = new FlowchartAuthoredStructure(
            activities: [new ActivityNode("first", "activity-first", [], []), new ActivityNode("second", "activity-second", [], [])],
            connections: [new(new("first", "approved"), new("second"), "first")],
            startNodeId: "first",
            nodeMetadata: new Dictionary<string, FlowchartNodeMetadata>
            {
                ["first"] = new("policy", new Dictionary<string, string> { ["externalId"] = "first" })
            });
        var node = new ActivityNode(
            "flow",
            "activity-flowchart",
            [],
            [],
            new ActivityNodeStructure(
                FlowchartActivity.StructureKind,
                FlowchartActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(authored)));
        var compiled = service.CompileExecutableStructure(node)!;

        var remapped = service.RemapExecutableStructure(compiled, new Dictionary<string, string>
        {
            ["first"] = "node-first",
            ["second"] = "node-second"
        });
        var structure = remapped.Payload.Deserialize<FlowchartStructure>()!;
        var connection = Assert.Single(structure.Connections);

        Assert.Equal("node-first", structure.StartNodeId);
        Assert.Equal("node-first", connection.Source.NodeId);
        Assert.Equal("node-second", connection.Target.NodeId);
        Assert.Equal("first", connection.Id);
        Assert.Contains("node-first", structure.NodeMetadata.Keys);
    }

    [Theory]
    [InlineData("""[{"variableKey":"counter","name":"Counter","type":{"alias":"Int32"},"policy":{"isSensitive":true},"initialBinding":{"source":"literal","literal":{"inlineValue":42}},"future":{"nodeId":"first"}}]""")]
    [InlineData("""[{"referenceKey":"counter","name":"Counter","type":{"alias":"Int32"},"default":{"expressionType":"Literal","value":42},"future":{"nodeId":"first"}}]""")]
    [InlineData("""[{"futureDeclaration":{"nodeId":"first","policy":{"custom":true}}}]""")]
    public void RemapExecutableStructure_PreservesVariablesAndOpaqueDataAtEveryRewrittenLevel(string variables)
    {
        var payload = JsonSerializer.Deserialize<JsonElement>("""
            {"connections":[{"id":"first","source":{"nodeId":"first","port":" approved ","future":{"nodeId":"first"}},"target":{"nodeId":"external","future":{"child":"first"}},"future":{"nodeId":"first"}}],
             "startNodeId":"first","variables":__VARIABLES__,"nodeMetadata":{"first":{"policyKind":" policy ","configuration":{"externalId":"first"},"future":{"nodeId":"first"}}},
             "connectionMetadata":{"first":{"label":" retained ","future":{"nodeId":"first"}}},"future":{"nodeId":"first"}}
            """.Replace("__VARIABLES__", variables, StringComparison.Ordinal));
        var original = payload.GetRawText();
        var structure = new ActivityNodeStructure(FlowchartActivity.StructureKind, FlowchartActivity.StructureSchemaVersion, payload);

        var remapped = StructureService().RemapExecutableStructure(structure, new Dictionary<string, string> { ["first"] = "placed" }).Payload;

        Assert.Equal("placed", remapped.GetProperty("startNodeId").GetString());
        var before = payload.GetProperty("connections")[0]; var after = remapped.GetProperty("connections")[0];
        Assert.Equal("placed", after.GetProperty("source").GetProperty("nodeId").GetString());
        Assert.Equal(" approved ", after.GetProperty("source").GetProperty("port").GetString());
        Assert.True(JsonElement.DeepEquals(before.GetProperty("source").GetProperty("future"), after.GetProperty("source").GetProperty("future")));
        foreach (var name in new[] { "id", "target", "future" }) Assert.True(JsonElement.DeepEquals(before.GetProperty(name), after.GetProperty(name)));
        Assert.True(JsonElement.DeepEquals(payload.GetProperty("nodeMetadata").GetProperty("first"), remapped.GetProperty("nodeMetadata").GetProperty("placed")));
        Assert.False(remapped.GetProperty("nodeMetadata").TryGetProperty("first", out _));
        foreach (var name in new[] { "variables", "connectionMetadata", "future" }) Assert.True(JsonElement.DeepEquals(payload.GetProperty(name), remapped.GetProperty(name)));
        Assert.Equal(original, payload.GetRawText());
    }

    [Theory]
    [InlineData("""{"connections":{}}""")]
    [InlineData("""{"startNodeId":42}""")]
    [InlineData("""{"nodeMetadata":[]}""")]
    [InlineData("""{"connectionMetadata":[]}""")]
    [InlineData("""{"variables":{}}""")]
    public void RemapExecutableStructure_StillRefusesMalformedKnownFields(string json)
    {
        var structure = new ActivityNodeStructure(FlowchartActivity.StructureKind, FlowchartActivity.StructureSchemaVersion, JsonSerializer.Deserialize<JsonElement>(json));
        Assert.Throws<InvalidOperationException>(() => StructureService().RemapExecutableStructure(structure,
            new Dictionary<string, string> { ["first"] = "placed" }));
        Assert.Equal(json, structure.Payload.GetRawText());
    }

    [Fact]
    public void RemapExecutableStructure_StillRefusesAnInvalidEndpointContract()
    {
        const string json = """{"connections":[{"source":{"nodeId":" "},"target":{"nodeId":"second"}}]}""";
        var structure = new ActivityNodeStructure(FlowchartActivity.StructureKind, FlowchartActivity.StructureSchemaVersion, JsonSerializer.Deserialize<JsonElement>(json));
        var failure = Record.Exception(() => StructureService().RemapExecutableStructure(structure, new Dictionary<string, string> { ["first"] = "placed" }));
        Assert.True(failure is ArgumentException or JsonException, failure?.ToString());
        Assert.Equal(json, structure.Payload.GetRawText());
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public void RemapExecutableStructure_StillRefusesAnInvalidPlacedEndpointIdentity(string? placed)
    {
        var payload = JsonSerializer.Deserialize<JsonElement>("""{"connections":[{"source":{"nodeId":"first"},"target":{"nodeId":"second"}}]}""");
        var structure = new ActivityNodeStructure(FlowchartActivity.StructureKind, FlowchartActivity.StructureSchemaVersion, payload);
        Assert.ThrowsAny<ArgumentException>(() => StructureService().RemapExecutableStructure(structure,
            new Dictionary<string, string> { ["first"] = placed! }));
    }
}
