using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Activities.Flowchart.Models;
using Elsa.Activities.Sequence.Models;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;
using FlowchartActivity = Elsa.Activities.Flowchart.Activities.Flowchart;
using SequenceActivity = Elsa.Activities.Sequence.Activities.Sequence;

namespace Elsa.Workflows.Publishing.Api.Tests;

public sealed partial class PublishWorkflowRequestHandlerTests
{
    private static readonly JsonSerializerOptions DeclarationOptions = new(JsonSerializerDefaults.Web)
    { Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) } };

    // A reusable body's native container local is compiled to the canonical declaration (key, policy, initial binding).
    // Placement remaps the owner's child ids through the owner's structure handler; the declaration must survive that
    // rewrite so the placed Set still targets a declared, initialized local of its placed owner.
    [Theory]
    [InlineData("sequence", false)]
    [InlineData("sequence", true)]
    [InlineData("flowchart", false)]
    [InlineData("flowchart", true)]
    public async Task A_placed_native_container_local_keeps_its_canonical_declaration(string owner, bool nested)
    {
        var local = new VariableDefinition("local", "Local", new TypeReference("String"), null, new ArgumentValue("start", "Literal"));
        var assignment = new ActivityNode("assign-local", "elsa.intrinsic.set@1",
            [new ArgumentState("value", new ArgumentValue("one", "Literal"), null, null, null, null)], [])
        {
            Intrinsic = new(AuthoredWorkflowIntrinsicKind.Set, new TypeReference("Elsa.Any"), new("local", "body"))
        };
        var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        ActivityNode[] children = [new ActivityNode("producer", CustomerProducerVersion.Id, [], []), assignment];
        var body = owner == "sequence"
            ? new ActivityNode("body", "activity-sequence", Inputs: [], Outputs: [],
                Structure: new ActivityNodeStructure(SequenceActivity.StructureKind, SequenceActivity.StructureSchemaVersion,
                    JsonSerializer.SerializeToElement(new SequenceAuthoredStructure(children, [local]), web)))
            : new ActivityNode("body", "activity-flowchart", Inputs: [], Outputs: [],
                Structure: new ActivityNodeStructure(FlowchartActivity.StructureKind, FlowchartActivity.StructureSchemaVersion,
                    JsonSerializer.SerializeToElement(new FlowchartAuthoredStructure(children,
                        [new FlowchartConnection(new FlowchartEndpoint("producer", "Done"), new FlowchartEndpoint("assign-local", null))],
                        "producer", variables: [local]), web)));

        var executable = await PublishPlacedBodyAsync(BodyRead(null), nested: nested, bodyOverride: body);

        var nodes = executable.RootActivity.DescendantsAndSelf().ToArray();
        var setters = nodes.Where(node => node.IntrinsicKind == WorkflowIntrinsicKind.Set).ToArray();
        Assert.Equal(nested ? 2 : 1, setters.Length);
        foreach (var setter in setters)
        {
            var declaringOwner = Assert.Single(nodes, node => node.ExecutableNodeId == setter.IntrinsicVariable!.DeclaringScopeId);
            Assert.Contains(declaringOwner.ChildSlots.SelectMany(slot => slot.Activities), child => child.ExecutableNodeId == setter.ExecutableNodeId);
            var declarations = declaringOwner.Structure!.Payload.GetProperty("variables").Deserialize<RuntimeVariableDeclaration[]>(DeclarationOptions)!;
            var declaration = Assert.Single(declarations);
            Assert.Equal("local", declaration.VariableKey);
            Assert.Equal("String", declaration.Type.Alias);
            Assert.Equal(DurableValueStorage.Inline, declaration.Policy.Storage);
            Assert.Equal("start", declaration.InitialBinding!.Literal!.InlineValue!.Value.GetString());
            Assert.Equal("one", setter.InputBindings["value"].Literal!.InlineValue!.Value.GetString());
        }
    }
}
