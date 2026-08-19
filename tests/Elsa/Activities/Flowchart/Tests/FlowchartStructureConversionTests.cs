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
        new ActivitiesFlowchartRuntimeFeature().ConfigureServices(services);
        new Elsa.Activities.Flowchart.Design.ActivitiesFlowchartDesignFeature().ConfigureServices(services);
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
}
