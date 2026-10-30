using System.Text.Json;
using Elsa.Activities.Bpmn.Activities;
using Elsa.Activities.Bpmn.Internal;
using Elsa.Activities.Bpmn.Models;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText;
using Elsa.Serialization.Testing;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Services;
using Elsa.Workflows.Publishing.Services;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Values;
using Xunit;

namespace Elsa.Activities.Bpmn.Tests;

public sealed class BpmnDecisionPublicationTests
{
    private static readonly JsonSerializerOptions Web = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData("High", "exclusiveGateway", "high")]
    [InlineData(" Low ", "exclusiveGateway", "low")]
    [InlineData("", "exclusiveGateway", "low")]
    [InlineData("Unexpected", "exclusiveGateway", "low")]
    [InlineData("High", "inclusiveGateway", "high")]
    public async Task Authored_decision_compiles_and_routes_through_production_activation(string outcome, string gatewayType, string expected)
    {
        await using var fixture = await BpmnRuntimeFixture.CreateAsync(["process-exec", "decision-exec", "task-exec"], services =>
        {
            new SerializationFeature().ConfigureServices(services);
            new Elsa.Activities.Primitives.ActivitiesPrimitivesFeature().ConfigureServices(services);
        });
        var compiled = Compile(outcome, gatewayType);
        var decision = compiled.ChildSlots.Single().Activities.Single(node => node.AuthoredActivityId == "decision");
        Assert.Contains("High", decision.ActivityContract!.Outcomes);
        Assert.Contains("Done", decision.ActivityContract.Outcomes);
        var run = await fixture.RunAsync(WorkflowExecutionHarness.NewExecutable(compiled));
        run.AssertRan(expected);
        run.AssertDidNotRun(expected == "high" ? "low" : "high");
        run.AssertWorkflowCompleted();
    }

    [Fact]
    public void Conditional_names_are_scoped_to_the_bound_decision_and_do_not_relax_other_contracts()
    {
        var compiled = Compile("High", "exclusiveGateway", foreignTaskCondition: true);
        var children = compiled.ChildSlots.Single().Activities;
        Assert.DoesNotContain("High", children.Single(node => node.AuthoredActivityId == "high").ActivityContract!.Outcomes);
        Assert.DoesNotContain("Foreign", children.Single(node => node.AuthoredActivityId == "high").ActivityContract!.Outcomes);
        Assert.DoesNotContain("Foreign", children.Single(node => node.AuthoredActivityId == "decision").ActivityContract!.Outcomes);
    }

    private static ExecutableNode Compile(string outcome, string gatewayType, bool foreignTaskCondition = false)
    {
        var serializer = SerializationServices.PayloadSerializer();
        var registry = ClrConstruction.RegistryFor(typeof(BpmnProcess), typeof(BpmnDecision), typeof(WriteLine));
        var versions = new[]
        {
            Version(typeof(BpmnProcess)),
            Version(typeof(BpmnDecision), new InputDefinition("Outcome", "Outcome", new TypeReference("String"), null, "Outcome", null, IsNullable: true)),
            Version(typeof(WriteLine), new InputDefinition("text", "Text", new TypeReference("String"), null, "Text", null, IsNullable: false))
        }.ToDictionary(version => version.Id);
        ActivityNode Leaf(string id, Type type, string inputKey, string value) => new(id, type.FullName!,
            [new ArgumentState(inputKey, new ArgumentValue(JsonSerializer.SerializeToElement(value), "Literal"), null, null, null, null)], []);
        var decision = Leaf("decision", typeof(BpmnDecision), "Outcome", outcome);
        var high = Leaf("high", typeof(WriteLine), "text", "High order");
        var low = Leaf("low", typeof(WriteLine), "text", "Default order");
        var structure = new BpmnAuthoredStructure([decision, high, low],
            [new("start", "startEvent"), new("gateway", gatewayType, childNodeId: "decision"),
             new("high-task", "task", childNodeId: "high"), new("low-task", "task", childNodeId: "low"),
             new("high-end", "endEvent"), new("low-end", "endEvent")],
            [new("start-flow", "start", "gateway"), new("high-flow", "gateway", "high-task", conditionOutcome: "High"),
             new("default-flow", "gateway", "low-task", isDefault: true),
             new("high-end-flow", "high-task", "high-end", conditionOutcome: foreignTaskCondition ? "Foreign" : null), new("low-end-flow", "low-task", "low-end")]);
        var root = new ActivityNode(BpmnRuntimeFixture.ProcessNodeId, typeof(BpmnProcess).FullName!, [], [],
            new(BpmnProcess.StructureKind, BpmnProcess.StructureSchemaVersion, JsonSerializer.SerializeToElement(structure, Web)));
        var structureService = new DefaultActivityStructureService([new BpmnStructureHandler()]);
        var compiler = new ExecutableNodeCompiler(structureService, registry, new RuntimeInputBindingCompiler(registry),
            new RuntimeOutputCaptureCompiler(new RuntimeDurableValueStorageDriverRegistry([new JsonRuntimeDurableValueStorageDriver()])));
        return compiler.CompileRoot(root, new ActivityTreeProjector(structureService).Project(root), versions);

        ActivityDefinitionVersion Version(Type type, params InputDefinition[] inputs) => new("1.0.0", $"definition:{type.Name}")
        {
            Id = type.FullName!,
            Definition = new ActivityDefinition { Id = $"definition:{type.Name}", ActivityTypeKey = type.FullName!, Category = "BPMN" },
            ProviderKey = WellKnownRuntimeActivityConsumers.ClrActivity,
            ProviderSchemaVersion = RuntimeActivityDescriptor.InitialSchemaVersion,
            ConsumerKey = WellKnownRuntimeActivityConsumers.ClrActivity,
            ConsumerSchemaVersion = RuntimeActivityDescriptor.InitialSchemaVersion,
            DescriptorPayload = ClrConstruction.Payload(serializer, type),
            Inputs = inputs
        };
    }
}
