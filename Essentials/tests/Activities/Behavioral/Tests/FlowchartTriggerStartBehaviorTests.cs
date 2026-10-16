using System.Text.Json;
using Elsa.Activities.Behavioral.Infrastructure;
using Elsa.Activities.Flowchart;
using Elsa.Activities.Flowchart.Models;
using Elsa.Activities.Http;
using Elsa.Activities.Http.Activities;
using Elsa.Activities.Http.Models;
using Elsa.Activities.Primitives;
using Elsa.Activities.Scheduling.Activities;
using Elsa.Activities.Testing;
using Elsa.Serialization.SystemText;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using FlowchartActivity = Elsa.Activities.Flowchart.Activities.Flowchart;

namespace Elsa.Activities.Behavioral;

/// <summary>
/// Spec 238, User Story 1: a Flowchart with several start triggers runs from the trigger that fired. The trigger
/// that did not fire never runs, so it leaves no bookmark behind.
/// </summary>
public sealed class FlowchartTriggerStartBehaviorTests
{
    private static readonly string[] ActivityExecutionIds = Enumerable.Range(1, 16).Select(index => $"actexec-{index}").ToArray();

    [Fact]
    public async Task TwoHttpEndpoints_SecondFires_RunsSecondBranchOnly_NoBookmarkLeft()
    {
        await using var harness = NewHarness();
        var executable = TwoBranchFlowchart(
            Endpoint("node-endpoint-a", "/orders"), "node-after-a",
            Endpoint("node-endpoint-b", "/invoices"), "node-after-b");

        var run = await harness.RunAsync(executable, Request("/invoices"), triggerNodeId: "node-endpoint-b");

        run.AssertWorkflowCompleted();
        run.AssertRan("node-endpoint-b", "node-after-b");
        run.AssertDidNotRun("node-endpoint-a", "node-after-a");
        Assert.Empty(await BookmarksAsync(harness));
    }

    [Fact]
    public async Task CronStartNode_HttpEndpointFires_RunsEndpointBranchOnly()
    {
        await using var harness = NewHarness();
        var executable = TwoBranchFlowchart(
            CronNode("node-cron"), "node-after-cron",
            Endpoint("node-endpoint", "/orders"), "node-after-endpoint");

        var run = await harness.RunAsync(executable, Request("/orders"), triggerNodeId: "node-endpoint");

        run.AssertWorkflowCompleted();
        run.AssertRan("node-endpoint", "node-after-endpoint");
        run.AssertDidNotRun("node-cron", "node-after-cron");
        Assert.Empty(await BookmarksAsync(harness));
    }

    [Fact]
    public async Task CronStartNode_CronFires_RunsCronBranchOnly()
    {
        await using var harness = NewHarness();
        var executable = TwoBranchFlowchart(
            CronNode("node-cron"), "node-after-cron",
            Endpoint("node-endpoint", "/orders"), "node-after-endpoint");

        var run = await harness.RunAsync(executable, JsonSerializer.SerializeToElement(new { }), triggerNodeId: "node-cron");

        run.AssertWorkflowCompleted();
        run.AssertRan("node-cron", "node-after-cron");
        run.AssertDidNotRun("node-endpoint", "node-after-endpoint");
        Assert.Empty(await BookmarksAsync(harness));
    }

    /// <summary>The first branch's head is the start node; the second branch's head is the other start trigger.</summary>
    private static WorkflowExecutable TwoBranchFlowchart(ExecutableNode firstHead, string firstNext, ExecutableNode secondHead, string secondNext)
    {
        var root = Nodes.Structural(
            "node-flowchart",
            typeof(FlowchartActivity),
            FlowchartActivity.StructureKind,
            FlowchartActivity.StructureSchemaVersion,
            new FlowchartStructure(
                [
                    new FlowchartConnection(new FlowchartEndpoint(firstHead.ExecutableNodeId, null), new FlowchartEndpoint(firstNext)),
                    new FlowchartConnection(new FlowchartEndpoint(secondHead.ExecutableNodeId, null), new FlowchartEndpoint(secondNext))
                ],
                firstHead.ExecutableNodeId),
            [
                new ExecutableChildSlot(FlowchartActivity.ActivitiesSlotName,
                [
                    firstHead,
                    WorkflowExecutionHarness.NewProbeNode(firstNext),
                    secondHead,
                    WorkflowExecutionHarness.NewProbeNode(secondNext)
                ])
            ]);
        return WorkflowExecutionHarness.NewExecutable(root);
    }

    private static ExecutableNode Endpoint(string nodeId, string path) =>
        Nodes.Leaf(nodeId, typeof(HttpEndpoint), new Dictionary<string, object?>
        {
            ["Path"] = path,
            ["CanStartWorkflow"] = true
        });

    private static ExecutableNode CronNode(string nodeId) =>
        Nodes.Leaf(nodeId, typeof(Cron), new Dictionary<string, object?> { ["Expression"] = "0 0 * * *" });

    private static JsonElement Request(string path) =>
        JsonSerializer.SerializeToElement(new HttpRequestModel(
            Path: path,
            Method: "GET",
            Headers: new Dictionary<string, string[]>(),
            Query: new Dictionary<string, string[]>(),
            Body: null,
            RouteData: new Dictionary<string, string>()));

    private static WorkflowExecutionHarness NewHarness() =>
        WorkflowExecutionHarness.Create()
            .WithFeature(services => new SerializationFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesPrimitivesFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesHttpFeature().ConfigureServices(services))
            .WithFeature(services => new Workflows.Runtime.Scheduling.WorkflowsRuntimeSchedulingFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesFlowchartFeature().ConfigureServices(services))
            .WithFeature(services => services.AddSingleton<Elsa.Http.Core.Contracts.IHttpRequestBodyParser, Elsa.Http.Services.HttpRequestBodyParser>())
            .Build(ActivityExecutionIds);

    private static async Task<IReadOnlyCollection<BookmarkState>> BookmarksAsync(WorkflowExecutionHarness harness) =>
        await harness.Services.GetRequiredService<IBookmarkStateStore>().ListAllBookmarkStatesAsync(WorkflowExecutionHarness.WorkflowExecutionId);
}
