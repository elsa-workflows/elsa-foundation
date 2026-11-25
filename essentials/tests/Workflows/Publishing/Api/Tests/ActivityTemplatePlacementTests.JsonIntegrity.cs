using System.Text.Json;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

public sealed partial class ActivityTemplatePlacementTests
{
    [Fact]
    public async Task Placement_preserves_raw_compiled_scoped_variable_initial_bindings_while_routing_children()
    {
        const string raw = """{ "escaped" : "\u0061\/\u003c", "number" : 12345678901234567890123456789012345, "fraction" : 1.0, "nested" : [ null, { "CaseKey" : "v" } ] }""";
        var type = new ValueTypeDescriptor("Object");
        var policy = ValueProtectionPolicy.InstanceInline;
        var declaration = new RuntimeVariableDeclaration("local", "Local", type, policy,
            new RuntimeInputBinding("local", type, policy, RuntimeInputBindingSource.Literal,
                literal: ValueEnvelope.Inline(type, Json(raw), policy)));
        var payload = JsonSerializer.SerializeToElement(new
        {
            activities = new[] { "leaf" },
            variables = new[] { declaration }
        }, JsonBaseline.Options);
        var sequenceOverlay = new ExecutableActivityTemplateOccurrenceOverlay("1", Json("[]"),
            new ExecutableActivityStructure("elsa.sequence.structure", "1.0.0", payload));
        var graph = Template("raw-graph", "raw-graph-hash", Node("graph-root"),
        [
            new("sequence-definition", "sequence-version", "1.0.0", "raw-sequence", "raw-sequence-hash", "sequence", ActivityInvocationOrigin.Empty,
                null, "activity-graph", 0, sequenceOverlay),
            new("leaf-definition", "leaf-version", "1.0.0", "raw-leaf", "raw-leaf-hash", "leaf", ActivityInvocationOrigin.Empty,
                "sequence", "Activities", 0, new("1", Json("[]"), null))
        ]);
        var sequence = Template("raw-sequence", "raw-sequence-hash", Node("sequence-root"));
        var leaf = Template("raw-leaf", "raw-leaf-hash", Node("leaf-root"));
        var publications = new[]
        {
            Publication("graph-definition", "graph-version", "type.graph", graph),
            Publication("sequence-definition", "sequence-version", "type.sequence", sequence),
            Publication("leaf-definition", "leaf-version", "type.leaf", leaf)
        };
        var references = publications.Select(publication => Reference(publication, ExecutableLayoutSidecar.Empty)).ToArray();
        var placer = CreatePlacer(new PublicationStore(publications), new TemplateReader([graph, sequence, leaf]),
            new SourceReader(references), new Sha256ActivityPlacementHasher());

        var first = await Place("call-a");
        var second = await Place("call-b");

        var firstSequence = AssertPlaced(first);
        var secondSequence = AssertPlaced(second);
        Assert.NotEqual(firstSequence.ExecutableNodeId, secondSequence.ExecutableNodeId);
        Assert.Equal(raw, payload.GetProperty("variables")[0].GetProperty("initialBinding").GetProperty("literal").GetProperty("inlineValue").GetRawText());

        async ValueTask<ActivityTemplatePlacement> Place(string call) => await placer.PlaceAsync(new(
            publications[0], graph, references[0],
            new([new(ActivityInvocationOriginSegmentKind.WorkflowRoot, "workflow"), new(ActivityInvocationOriginSegmentKind.AuthoredNode, call)]),
            "type.graph", new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, RuntimeOutputCapture>()));

        ExecutableNode AssertPlaced(ActivityTemplatePlacement placement)
        {
            var placedSequence = Assert.Single(Assert.Single(placement.Root.ChildSlots, slot => slot.Name == "activity-graph").Activities);
            var child = Assert.Single(Assert.Single(placedSequence.ChildSlots, slot => slot.Name == "Activities").Activities);
            var structure = Assert.IsType<ExecutableActivityStructure>(placedSequence.Structure).Payload;
            Assert.Equal(child.ExecutableNodeId, structure.GetProperty("activities")[0].GetString());
            var compiled = structure.GetProperty("variables")[0];
            Assert.Equal(raw, compiled.GetProperty("initialBinding").GetProperty("literal").GetProperty("inlineValue").GetRawText());
            var variable = Assert.Single(structure.GetProperty("variables").Deserialize<RuntimeVariableDeclaration[]>(JsonBaseline.Options)!);
            Assert.Equal("local", variable.VariableKey);
            Assert.Equal(RuntimeInputBindingSource.Literal, variable.InitialBinding!.Source);
            Assert.Equal(raw, variable.InitialBinding.Literal!.InlineValue!.Value.GetRawText());
            Assert.Equal(ValuePresence.Present, variable.InitialBinding.Literal.Presence);
            Assert.Equal(JsonValueKind.Null, variable.InitialBinding.Literal.InlineValue.Value.GetProperty("nested")[0].ValueKind);
            return placedSequence;
        }
    }
}
