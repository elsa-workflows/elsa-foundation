using System.Text.Json;
using Elsa.Activities.Flowchart.Exceptions;
using Elsa.Activities.Flowchart.Models;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using FlowchartActivity = Elsa.Activities.Flowchart.Activities.Flowchart;

namespace Elsa.Activities.Flowchart.Tests;

public sealed class FlowchartActivityTests : IDisposable
{
    private readonly ServiceProvider _serviceProvider = NewServiceProvider();

    [Fact]
    public async Task ExecuteAsync_CompletesWhenSlotHasNoChildren()
    {
        var context = NewContext(NewFlowchartNode([]));

        await ExecuteAsync(context);

        Assert.True(context.CompositeCompletionRequested);
        Assert.Equal([ActivityOutcomes.Done], context.CompositeCompletionOutcomeNames);
        Assert.Empty(context.GetChildActivityScheduleRequests());
    }

    [Fact]
    public async Task ExecuteAsync_SchedulesExplicitStartNode()
    {
        var root = NewFlowchartNode(
            [NewNode("node-a"), NewNode("node-b")],
            startNodeId: "node-b");
        var context = NewContext(root);

        await ExecuteAsync(context);

        var request = Assert.Single(context.GetChildActivityScheduleRequests());
        Assert.Equal("node-b", request.ExecutableNodeId);
        Assert.Equal("actexec-flowchart", request.SchedulingActivityExecutionId);
        Assert.False(context.CompositeCompletionRequested);
    }

    [Fact]
    public async Task ExecuteAsync_SelectsFirstChildWithoutInboundConnections()
    {
        var root = NewFlowchartNode(
            [NewNode("node-a"), NewNode("node-b")],
            connections: [NewConnection("node-a", "node-b")]);
        var context = NewContext(root);

        await ExecuteAsync(context);

        var request = Assert.Single(context.GetChildActivityScheduleRequests());
        Assert.Equal("node-a", request.ExecutableNodeId);
    }

    [Fact]
    public async Task ExecuteAsync_FallsBackToFirstChildWhenEveryChildHasInboundConnections()
    {
        var root = NewFlowchartNode(
            [NewNode("node-a"), NewNode("node-b")],
            connections: [NewConnection("node-a", "node-b"), NewConnection("node-b", "node-a")]);
        var context = NewContext(root);

        await ExecuteAsync(context);

        var request = Assert.Single(context.GetChildActivityScheduleRequests());
        Assert.Equal("node-a", request.ExecutableNodeId);
    }

    [Fact]
    public async Task OnChildCompletedAsync_SchedulesTargetsForDoneOutcome()
    {
        var root = NewFlowchartNode(
            [NewNode("node-a"), NewNode("node-b")],
            connections: [NewConnection("node-a", "node-b", sourcePort: null)]);
        var context = NewContext(root);
        var flowchart = new FlowchartActivity();

        await flowchart.OnChildCompletedAsync(new ActivityChildCompletedContext(context, "actexec-a", "node-a", []));

        var request = Assert.Single(context.GetChildActivityScheduleRequests());
        Assert.Equal("node-b", request.ExecutableNodeId);
        Assert.Equal("actexec-a", request.SchedulingActivityExecutionId);
        Assert.False(context.CompositeCompletionRequested);
    }

    [Fact]
    public async Task OnChildCompletedAsync_FollowsOnlyMatchingNamedOutcome()
    {
        var root = NewFlowchartNode(
            [NewNode("node-a"), NewNode("node-b"), NewNode("node-c")],
            connections:
            [
                NewConnection("node-a", "node-b", "Approved"),
                NewConnection("node-a", "node-c", "Rejected")
            ]);
        var context = NewContext(root);
        var flowchart = new FlowchartActivity();

        await flowchart.OnChildCompletedAsync(new ActivityChildCompletedContext(context, "actexec-a", "node-a", ["Rejected"]));

        var request = Assert.Single(context.GetChildActivityScheduleRequests());
        Assert.Equal("node-c", request.ExecutableNodeId);
    }

    [Fact]
    public async Task OnChildCompletedAsync_ProjectsLoopIterationKeyInSchedulingProvenance()
    {
        var root = NewFlowchartNode(
            [NewNode("node-a")],
            connections: [NewConnection("node-a", "node-a")]);
        var context = NewContext(root);
        var flowchart = new FlowchartActivity();

        await flowchart.OnChildCompletedAsync(new ActivityChildCompletedContext(context, "actexec-a", "node-a", [ActivityOutcomes.Done]));

        var request = Assert.Single(context.GetChildActivityScheduleRequests());
        Assert.Equal("node-a", request.ExecutableNodeId);
        Assert.Equal("node-a:1", request.SchedulingProvenance.IterationId);
        Assert.NotEqual(request.SchedulingProvenance.ExecutionPathId, request.SchedulingProvenance.IterationId);
    }

    [Fact]
    public async Task OnChildCompletedAsync_CompletesWhenNoOutboundConnectionMatches()
    {
        var root = NewFlowchartNode(
            [NewNode("node-a"), NewNode("node-b")],
            connections: [NewConnection("node-a", "node-b", "Approved")]);
        var context = NewContext(root);
        var flowchart = new FlowchartActivity();

        await flowchart.OnChildCompletedAsync(new ActivityChildCompletedContext(context, "actexec-a", "node-a", ["Rejected"]));

        Assert.Empty(context.GetChildActivityScheduleRequests());
        Assert.True(context.CompositeCompletionRequested);
        Assert.Equal([ActivityOutcomes.Done], context.CompositeCompletionOutcomeNames);
    }

    [Fact]
    public async Task ExecuteAsync_ThrowsWhenConnectionReferencesUnknownNode()
    {
        var root = NewFlowchartNode(
            [NewNode("node-a")],
            connections: [NewConnection("node-a", "missing")]);
        var context = NewContext(root);

        await Assert.ThrowsAsync<FlowchartExecutionException>(() => ExecuteAsync(context).AsTask());
    }

    [Fact]
    public async Task OnChildCompletedAsync_ThrowsWhenCompletionContainsDuplicateTargets()
    {
        var root = NewFlowchartNode(
            [NewNode("node-a"), NewNode("node-b")],
            connections:
            [
                NewConnection("node-a", "node-b"),
                NewConnection("node-a", "node-b")
            ]);
        var context = NewContext(root);
        var flowchart = new FlowchartActivity();

        await Assert.ThrowsAsync<FlowchartExecutionException>(() => flowchart
            .OnChildCompletedAsync(new ActivityChildCompletedContext(context, "actexec-a", "node-a", [ActivityOutcomes.Done]))
            .AsTask());
    }

    public void Dispose() => _serviceProvider.Dispose();

    private static ServiceProvider NewServiceProvider()
    {
        var services = new ServiceCollection();
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);
        new ActivitiesFlowchartFeature().ConfigureServices(services);
        return services.BuildServiceProvider();
    }

    private ValueTask ExecuteAsync(SimpleActivityExecutionContext context) =>
        ((IActivity)new FlowchartActivity()).ExecuteAsync(context);

    private SimpleActivityExecutionContext NewContext(ExecutableNode executableNode)
    {
        var activity = new FlowchartActivity { Id = "actexec-flowchart", NodeId = "node-flowchart" };
        return new SimpleActivityExecutionContext(
            _serviceProvider,
            activity,
            CancellationToken.None,
            "wfexec-1",
            NewIdentity(),
            NewWorkItem(),
            executableNode,
            NewRunningState());
    }

    private static ActivityExecutionState NewRunningState() =>
        new(
            Execution: new ActivityExecution("actexec-flowchart", "wfexec-1", "node-flowchart", "authored-flowchart", "flowchart", "1.0.0"),
            Status: ActivityExecutionStatus.Running,
            SubStatus: null,
            ScheduledAt: DateTimeOffset.UnixEpoch,
            StartedAt: DateTimeOffset.UnixEpoch,
            CompletedAt: null,
            SchedulingActivityExecutionId: null,
            ParentActivityExecutionId: null,
            BranchId: null,
            IterationId: null,
            CallStackDepth: 0,
            BookmarkIds: [],
            IncidentIds: [],
            FaultCount: 0,
            AggregateFaultCount: 0,
            Metadata: new Dictionary<string, string>());

    private static RuntimeSchedulerWorkItem NewWorkItem() =>
        new(
            workItemId: "work-flowchart",
            workflowExecutionId: "wfexec-1",
            commandId: "command-flowchart",
            commandKind: WorkflowExecutionCommandKind.InvokeActivity,
            envelopeId: "envelope-1",
            idempotencyKey: "wfexec-1:flowchart",
            enqueuedAt: DateTimeOffset.UnixEpoch,
            recordedAt: DateTimeOffset.UnixEpoch,
            sequence: 1,
            payload: null,
            commandMetadata: new Dictionary<string, string>(),
            envelopeMetadata: new Dictionary<string, string>());

    private static ExecutableNode NewFlowchartNode(
        IReadOnlyCollection<ExecutableNode> children,
        IReadOnlyCollection<FlowchartConnection>? connections = null,
        string? startNodeId = null)
    {
        var structure = new ExecutableActivityStructure(
            FlowchartActivity.StructureKind,
            FlowchartActivity.StructureSchemaVersion,
            JsonSerializer.SerializeToElement(new FlowchartStructure(connections ?? [], startNodeId)));

        return NewNode(
            "node-flowchart",
            activityType: "flowchart",
            childSlots:
            [
                new ExecutableChildSlot(
                    FlowchartActivity.ActivitiesSlotName,
                    children)
            ],
            structure: structure);
    }

    private static ExecutableNode NewNode(
        string nodeId,
        string activityType = "test/probe",
        IReadOnlyCollection<ExecutableChildSlot>? childSlots = null,
        ExecutableActivityStructure? structure = null) =>
        new(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: activityType,
            activityTypeVersion: "1.0.0",
            descriptor: new RuntimeActivityDescriptor("test", RuntimeActivityDescriptor.InitialSchemaVersion, JsonSerializer.SerializeToElement(new { })),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>(),
            childSlots: childSlots,
            structure: structure);

    private static FlowchartConnection NewConnection(string sourceNodeId, string targetNodeId, string? sourcePort = null) =>
        new(new FlowchartEndpoint(sourceNodeId, sourcePort), new FlowchartEndpoint(targetNodeId));

    private static WorkflowExecutableIdentity NewIdentity() =>
        new("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test");
}
