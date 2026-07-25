using System.Text.Json;
using Elsa.Activities.Bpmn.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;
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
        new ActivitiesBpmnFeature().ConfigureServices(services);
        _provider = services.BuildServiceProvider();
        _handler = Assert.Single(_provider.GetServices<IActivityStructureHandler>());
    }

    public void Dispose() => _provider.Dispose();

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
