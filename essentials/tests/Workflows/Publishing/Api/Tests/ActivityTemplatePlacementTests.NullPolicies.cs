using System.Text.Json;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Publishing.Api.Tests;

public sealed partial class ActivityTemplatePlacementTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Nested_occurrence_literals_use_the_host_compilation_policy(bool enabled)
    {
        var overlay = new ExecutableActivityTemplateOccurrenceOverlay("1",
            Json("""[{"referenceKey":"condition","value":{"expressionType":"Literal","value":null}}]"""), null);
        var leaf = Template("template-condition", "hash-condition", new ExecutableNode(
            "source-root", "source-root", "type.sequence", "1", new("sequence", "1", Json("{}")),
            new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, RuntimeOutputCapture>(),
            new Dictionary<string, string>(), activityContract: RuntimeContractWithCondition()));
        var graph = Template("template-graph", "hash-graph", Node("graph-root"),
            [new("definition-condition", "version-condition", "1.0.0", leaf.TemplateId, leaf.TemplateHash,
                "condition", ActivityInvocationOrigin.Empty, null, "activity-graph", 0, overlay)]);
        var graphPublication = Publication("definition-graph", "version-graph", "type.graph", graph);
        var leafPublication = Publication("definition-condition", "version-condition", "type.sequence", leaf);
        var reference = Reference(graphPublication, ExecutableLayoutSidecar.Empty);
        var placer = CreatePlacer(new PublicationStore([graphPublication, leafPublication]), new TemplateReader([graph, leaf]),
            new SourceReader([reference, Reference(leafPublication, ExecutableLayoutSidecar.Empty)]), new Sha256ActivityPlacementHasher())
            .WithInputBindingCompiler(new RuntimeInputBindingCompiler(TestWellKnownTypeRegistry.Create()).WithValuePolicies(new(enabled, false)));
        var request = new ActivityTemplatePlacementRequest(graphPublication, graph, reference,
            new([new(ActivityInvocationOriginSegmentKind.WorkflowRoot, "workflow")]), graphPublication.ActivityTypeKey,
            new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, RuntimeOutputCapture>());
        if (!enabled)
        {
            var exception = await Assert.ThrowsAnyAsync<Exception>(() => placer.PlaceAsync(request).AsTask());
            Assert.Contains("VF-ACT-004", exception.ToString());
            return;
        }
        var placed = await placer.PlaceAsync(request);
        var child = Assert.Single(Assert.Single(placed.Root.ChildSlots).Activities);
        Assert.Equal(ValuePresence.ExplicitNull, child.InputBindings["condition"].Literal!.Presence);
        Assert.Equal(JsonValueKind.Null, child.InputBindings["condition"].LiteralValue!.Value.ValueKind);
    }

    [Fact]
    public void Neutral_templates_retain_supported_nulls_but_never_default_a_variable_declaration()
    {
        var compiler = new RuntimeInputBindingCompiler(TestWellKnownTypeRegistry.Create());
        var port = (IActivityTemplateInputBindingCompiler)compiler;
        var definition = new Elsa.Activities.Design.Core.Models.InputDefinition("value", "Value", new("Int32"), null, "Value", null, false);
        Elsa.Workflows.Design.Core.Models.ArgumentState[] states =
            [new("value", new Elsa.Expressions.Core.Models.ArgumentValue(Json("null"), "Literal"), null, null, null, null)];
        Assert.Throws<ArgumentException>(() => compiler.CompileAll("node", [definition], states));
        var neutral = port.CompileGraphInputs("node", [definition], states);
        Assert.Equal(ValuePresence.ExplicitNull, neutral["value"].Literal!.Presence);
        var variable = new Elsa.Expressions.Core.Models.VariableDefinition("count", "Count", new("Int32"), null,
            new Elsa.Expressions.Core.Models.ArgumentValue(Json("null"), "Literal"));
        var exception = Assert.Throws<ArgumentException>(() => port.CompileGraphVariables("scope", [variable]));
        Assert.Contains("VF-ACT-004", exception.Message);
    }
}
