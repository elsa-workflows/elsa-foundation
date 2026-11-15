using System.Text.Json;
using Elsa.Activities.Bpmn.Core;
using Elsa.Activities.Bpmn.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using BpmnProcessActivity = Elsa.Activities.Bpmn.Activities.BpmnProcess;

namespace Elsa.Activities.Bpmn.Tests;

public sealed class BpmnStructureHandlerTests : IDisposable
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly ServiceProvider _provider;
    private readonly IActivityStructureHandler _handler;

    public BpmnStructureHandlerTests()
    {
        var services = new ServiceCollection();
        new Elsa.Activities.Bpmn.Design.ActivitiesBpmnDesignFeature().ConfigureServices(services);
        _provider = services.BuildServiceProvider();
        _handler = Assert.Single(_provider.GetServices<IActivityStructureHandler>(), handler =>
            handler.Kind == BpmnProcessActivity.StructureKind && handler.SchemaVersion == BpmnProcessActivity.StructureSchemaVersion);
    }

    public void Dispose() => _provider.Dispose();

    [Theory]
    [InlineData("2.0.0")]
    [InlineData("99.0.0")]
    public void Legacy_handler_refuses_other_versions_before_typed_deserialization(string schema)
    {
        var payload = JsonSerializer.SerializeToElement(new { documentXml = "complete opaque source", processId = "p", activities = Array.Empty<ActivityNode>() });
        var source = new ActivityNode("process", "bpmn-version", [], [], new(BpmnProcessActivity.StructureKind, schema, payload));
        Assert.Throws<InvalidOperationException>(() => _handler.ProjectChildren(source));
        Assert.Throws<InvalidOperationException>(() => _handler.ProjectScopedVariables(source));
        Assert.Throws<InvalidOperationException>(() => _handler.ReplaceChildren(source, []));
        Assert.Throws<InvalidOperationException>(() => _handler.CompileExecutableStructure(source));
        Assert.Throws<InvalidOperationException>(() => _handler.RemapExecutableStructure(source.Structure!, new Dictionary<string, string> { ["child"] = "copy" }));
        Assert.Equal(payload.GetRawText(), source.Structure!.Payload.GetRawText());
    }

    [Theory]
    [InlineData("documentXml")]
    [InlineData("processId")]
    [InlineData("bindings")]
    [InlineData("scopeHeaders")]
    public void Legacy_handler_refuses_complete_source_authority_even_when_relabelled_schema1(string field)
    {
        foreach (var spelling in new[] { field, field.ToUpperInvariant() })
        {
            var payload = JsonSerializer.SerializeToElement(new Dictionary<string, object?> { [spelling] = "retained source authority" });
            var source = new ActivityNode("process", "bpmn-version", [], [], new(BpmnProcessActivity.StructureKind, BpmnProcessActivity.StructureSchemaVersion, payload));
            var failure = Assert.Throws<BpmnDocumentSourceException>(() => _handler.ProjectChildren(source));
            Assert.Contains(spelling, failure.Message, StringComparison.Ordinal);
            Assert.Contains(source.NodeId, failure.Message, StringComparison.Ordinal);
            Assert.Contains(spelling, Assert.IsType<JsonException>(failure.InnerException).Message, StringComparison.Ordinal);
            Assert.Throws<BpmnDocumentSourceException>(() => _handler.ProjectScopedVariables(source));
            Assert.Throws<BpmnDocumentSourceException>(() => _handler.ReplaceChildren(source, []));
            Assert.Throws<BpmnDocumentSourceException>(() => _handler.CompileExecutableStructure(source));
            var remapFailure = Assert.Throws<BpmnDocumentSourceException>(() => _handler.RemapExecutableStructure(source.Structure!, new Dictionary<string, string>()));
            Assert.Contains(source.Structure!.Kind, remapFailure.Message, StringComparison.Ordinal);
            Assert.Contains(spelling, Assert.IsType<JsonException>(remapFailure.InnerException).Message, StringComparison.Ordinal);
            Assert.Equal(payload.GetRawText(), source.Structure!.Payload.GetRawText());
        }
    }

    [Fact]
    public void Legacy_guard_does_not_classify_fields_inside_native_literals_or_opaque_diagram_data()
    {
        var opaque = JsonSerializer.SerializeToElement(new { documentXml = "literal", PROCESSID = "literal", bindings = new[] { "literal" }, scopeHeaders = new { literal = true } });
        var child = new ActivityNode("child", "child-version", [new("value", new(opaque, "Literal"), null, null, null, null)], []);
        var authored = new BpmnAuthoredStructure([child], [new("task", BpmnElementTypes.Task, childNodeId: child.NodeId)], diagram: opaque);
        var source = new ActivityNode("process", "bpmn-version", [], [], new(BpmnProcessActivity.StructureKind, BpmnProcessActivity.StructureSchemaVersion,
            JsonSerializer.SerializeToElement(authored, SerializerOptions)));
        var slot = Assert.Single(_handler.ProjectChildren(source));
        var updated = _handler.ReplaceChildren(source, [slot]);
        var payload = updated.Structure!.Payload;
        Assert.True(JsonElement.DeepEquals(opaque, payload.GetProperty("diagram")));
        Assert.True(JsonElement.DeepEquals(opaque, payload.GetProperty("activities")[0].GetProperty("inputs")[0].GetProperty("value").GetProperty("value")));
        var compiled = _handler.CompileExecutableStructure(updated);
        var remapped = _handler.RemapExecutableStructure(compiled, new Dictionary<string, string> { ["child"] = "copy" });
        Assert.Equal("copy", remapped.Payload.GetProperty("elements")[0].GetProperty("childNodeId").GetString());
    }

    [Theory]
    [InlineData("\"set\"", "\"json\"")]
    [InlineData("\"set\"", "2")]
    [InlineData("0", "\"json\"")]
    [InlineData("0", "2")]
    public void AuthoredEnums_SupportStudioStringsAndExistingNumbers(string kind, string conversion)
    {
        using var document = JsonDocument.Parse($$"""
            {
              "activities": [{
                "nodeId": "set-variable", "activityVersionId": "elsa.intrinsic.set@1",
                "inputs": [{
                  "referenceKey": "value", "value": { "value": 42, "expressionType": "Literal" },
                  "conversion": { "mode": {{conversion}} }
                }],
                "outputs": [],
                "intrinsic": {
                  "kind": {{kind}}, "valueType": { "alias": "Int32" },
                  "variable": { "referenceKey": "counter", "declaringScopeId": "process" }
                }
              }],
              "variables": [{ "referenceKey": "counter", "name": "Counter", "type": { "alias": "Int32" } }]
            }
            """);
        var activity = new ActivityNode("process", "bpmn-version", [], [],
            new(BpmnProcessActivity.StructureKind, BpmnProcessActivity.StructureSchemaVersion, document.RootElement.Clone()));

        // Both the variable-picker traversal and draft-admission traversal read this payload.
        var service = new DefaultActivityStructureService([_handler]);
        var visible = new ScopedVariableResolver(service).Resolve([], activity, 100).GetVisibleVariables("set-variable");
        var variable = Assert.Single(visible);
        Assert.Equal("process", variable.ScopeId);
        Assert.Equal("counter", variable.Variable.ReferenceKey);
        var slot = Assert.Single(_handler.ProjectChildren(activity));
        AssertChild(Assert.Single(slot.Activities));

        var updated = _handler.ReplaceChildren(activity, [slot]);
        AssertChild(Assert.Single(Assert.Single(_handler.ProjectChildren(updated)).Activities));
        var compiled = _handler.CompileExecutableStructure(updated);
        Assert.Equal("counter", Assert.Single(compiled.Payload.Deserialize<BpmnStructure>(SerializerOptions)!.Variables).ReferenceKey);

        static void AssertChild(ActivityNode child)
        {
            Assert.Equal(AuthoredWorkflowIntrinsicKind.Set, child.Intrinsic!.Kind);
            Assert.Equal("counter", child.Intrinsic.Variable!.ReferenceKey);
            Assert.Equal("process", child.Intrinsic.Variable.DeclaringScopeId);
            Assert.Equal(AuthoredValueConversionMode.Json, Assert.Single(child.Inputs).Conversion!.Mode);
        }
    }

    [Fact]
    public void ReplaceChildren_PreservesCollaborationMetadata()
    {
        var authored = new BpmnAuthoredStructure(
            activities: [new ActivityNode("Child1", "activity-version-1", [], [])],
            pools:
            [
                new BpmnPool("Pool_Order", "Order Handling", "Process_Order"),
                new BpmnPool("Pool_Shipping", "Shipping", processRef: null, isExecutable: false)
            ],
            isTransaction: true,
            messageFlows:
            [
                new BpmnMessageFlow(
                    "Flow_OrderPlaced",
                    "Order placed",
                    sourceElementId: "Task_PlaceOrder",
                    sourcePoolId: "Pool_Order",
                    targetElementId: null,
                    targetPoolId: "Pool_Shipping",
                    messageName: "OrderPlaced")
            ]);
        var activity = new ActivityNode(
            "Process1",
            "bpmn-process-version-1",
            [],
            [],
            new ActivityNodeStructure(
                BpmnProcessActivity.StructureKind,
                BpmnProcessActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(authored, SerializerOptions)));
        var replacementChild = new ActivityNode("Child2", "activity-version-2", [], []);

        var result = _handler.ReplaceChildren(
            activity,
            [new ActivityChildProjection(BpmnProcessActivity.ActivitiesSlotName, [replacementChild])]);

        Assert.NotNull(result.Structure);
        var roundTripped = result.Structure.Payload.Deserialize<BpmnAuthoredStructure>(SerializerOptions)!;
        Assert.Equal("Child2", Assert.Single(roundTripped.Activities).NodeId);
        Assert.True(roundTripped.IsTransaction);

        Assert.Collection(
            roundTripped.Pools,
            pool =>
            {
                Assert.Equal("Pool_Order", pool.PoolId);
                Assert.Equal("Order Handling", pool.Name);
                Assert.Equal("Process_Order", pool.ProcessRef);
                Assert.True(pool.IsExecutable);
            },
            pool =>
            {
                Assert.Equal("Pool_Shipping", pool.PoolId);
                Assert.Null(pool.ProcessRef);
                Assert.False(pool.IsExecutable);
            });

        var messageFlow = Assert.Single(roundTripped.MessageFlows);
        Assert.Equal("Flow_OrderPlaced", messageFlow.FlowId);
        Assert.Equal("Order placed", messageFlow.Name);
        Assert.Equal("Task_PlaceOrder", messageFlow.SourceElementId);
        Assert.Equal("Pool_Order", messageFlow.SourcePoolId);
        Assert.Null(messageFlow.TargetElementId);
        Assert.Equal("Pool_Shipping", messageFlow.TargetPoolId);
        Assert.Equal("OrderPlaced", messageFlow.MessageName);
    }

    [Fact]
    public void RemapExecutableStructure_RewritesOnlyElementActivityBindings()
    {
        var authored = new BpmnAuthoredStructure(
            activities: [new ActivityNode("task-child", "task-version", [], []), new ActivityNode("listener-child", "listener-version", [], [])],
            elements:
            [
                new BpmnElement(
                    "Task_One",
                    BpmnElementTypes.Task,
                    childNodeId: "task-child",
                    listenerNodeId: "listener-child",
                    properties: new Dictionary<string, string> { ["externalId"] = "task-child" })
            ]);
        var activity = new ActivityNode(
            "Process1",
            "bpmn-process-version-1",
            [],
            [],
            new ActivityNodeStructure(
                BpmnProcessActivity.StructureKind,
                BpmnProcessActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(authored, SerializerOptions)));
        var compiled = _handler.CompileExecutableStructure(activity);

        var remapped = _handler.RemapExecutableStructure(compiled, new Dictionary<string, string>
        {
            ["task-child"] = "node-task",
            ["listener-child"] = "node-listener"
        });
        var element = Assert.Single(remapped.Payload.Deserialize<BpmnStructure>(SerializerOptions)!.Elements);

        Assert.Equal("node-task", element.ChildNodeId);
        Assert.Equal("node-listener", element.ListenerNodeId);
        Assert.Equal("task-child", element.Properties["externalId"]);
    }
}
