using System.Text.Json;
using Elsa.Activities.Flowchart.Models;
using Elsa.Activities.Runtime;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Runtime.Services;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using FlowchartActivity = Elsa.Activities.Flowchart.Activities.Flowchart;

namespace Elsa.Activities.Flowchart.Tests;

public sealed class FlowchartRuntimeTests
{
    private readonly DateTimeOffset _now = new(2026, 6, 12, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task FlowchartRoot_SchedulesStartChildAndPersistsParentExecutionId()
    {
        await using var provider = await NewProviderAsync(["actexec-flowchart", "actexec-a"]);
        var executable = NewExecutable(
            children: [NewProbeNode("node-a")],
            connections: []);

        await ExecuteAsync(provider, executable);

        var states = await provider.GetRequiredService<IActivityExecutionStateStore>().ListAsync("wfexec-1");
        var flowchartState = Assert.Single(states, state => state.Execution.ExecutableNodeId == "node-flowchart");
        var childState = Assert.Single(states, state => state.Execution.ExecutableNodeId == "node-a");
        Assert.Equal(ActivityExecutionStatus.Completed, flowchartState.Status);
        Assert.Equal("actexec-flowchart", childState.ParentActivityExecutionId);
        Assert.Equal("actexec-flowchart", childState.SchedulingActivityExecutionId);
    }

    [Fact]
    public async Task CompletedChildWithDone_SchedulesConnectedTarget()
    {
        await using var provider = await NewProviderAsync(["actexec-flowchart", "actexec-a", "actexec-b"]);
        var executable = NewExecutable(
            children: [NewProbeNode("node-a"), NewProbeNode("node-b")],
            connections: [NewConnection("node-a", "node-b")]);

        await ExecuteAsync(provider, executable);

        var states = await provider.GetRequiredService<IActivityExecutionStateStore>().ListAsync("wfexec-1");
        var targetState = Assert.Single(states, state => state.Execution.ExecutableNodeId == "node-b");
        Assert.Equal(ActivityExecutionStatus.Completed, targetState.Status);
        Assert.Equal("actexec-flowchart", targetState.ParentActivityExecutionId);
        Assert.Equal("actexec-a", targetState.SchedulingActivityExecutionId);
    }

    [Fact]
    public async Task NamedOutcome_FollowsOnlyMatchingConnection()
    {
        await using var provider = await NewProviderAsync(["actexec-flowchart", "actexec-a", "actexec-c"]);
        var executable = NewExecutable(
            children:
            [
                NewProbeNode("node-a", ["Rejected"]),
                NewProbeNode("node-b"),
                NewProbeNode("node-c")
            ],
            connections:
            [
                NewConnection("node-a", "node-b", "Approved"),
                NewConnection("node-a", "node-c", "Rejected")
            ]);

        await ExecuteAsync(provider, executable);

        var states = await provider.GetRequiredService<IActivityExecutionStateStore>().ListAsync("wfexec-1");
        Assert.DoesNotContain(states, state => state.Execution.ExecutableNodeId == "node-b");
        Assert.Contains(states, state => state.Execution.ExecutableNodeId == "node-c");
    }

    [Fact]
    public async Task TerminalChild_CompletesFlowchartAndWorkflow()
    {
        await using var provider = await NewProviderAsync(["actexec-flowchart", "actexec-a", "actexec-b"]);
        var executable = NewExecutable(
            children: [NewProbeNode("node-a"), NewProbeNode("node-b")],
            connections: [NewConnection("node-a", "node-b")]);

        await ExecuteAsync(provider, executable);

        var workflowState = await provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("wfexec-1");
        var states = await provider.GetRequiredService<IActivityExecutionStateStore>().ListAsync("wfexec-1");
        var flowchartState = Assert.Single(states, state => state.Execution.ExecutableNodeId == "node-flowchart");
        Assert.Equal(ActivityExecutionStatus.Completed, flowchartState.Status);
        Assert.Equal(WorkflowExecutionStatus.Completed, workflowState?.Status);
    }

    private async Task<ServiceProvider> NewProviderAsync(IEnumerable<string> activityExecutionIds)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IActivityConstructor, FlowchartActivityConstructor>();
        services.AddSingleton<IActivityConstructor, ProbeActivityConstructor>();
        services.AddSingleton<IRuntimeExecutionIdGenerator>(new DeterministicRuntimeExecutionIdGenerator(activityExecutionIds));
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);
        new ActivitiesRuntimeFeature().ConfigureServices(services);
        new ActivitiesFlowchartFeature().ConfigureServices(services);

        return services.BuildServiceProvider();
    }

    private async Task ExecuteAsync(ServiceProvider provider, WorkflowExecutable executable)
    {
        await provider.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(executable);
        var agent = await provider.GetRequiredService<IWorkflowExecutionActorProvider>()
            .GetAgentAsync(NewActivationRequest("wfexec-1"));

        var result = await agent.EnqueueAsync(NewStartEnvelope(executable.Identity));

        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted, result.Status);
        Assert.Empty(await provider.GetRequiredService<IWorkflowSchedulerWorkQueue>().ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
    }

    private WorkflowExecutable NewExecutable(
        IReadOnlyCollection<ExecutableNode> children,
        IReadOnlyCollection<FlowchartConnection> connections)
    {
        var root = new ExecutableNode(
            executableNodeId: "node-flowchart",
            authoredActivityId: "authored-flowchart",
            activityType: typeof(FlowchartActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: FlowchartActivityConstructor.DescriptorTypeKey,
            descriptorPayload: JsonSerializer.SerializeToElement(new FlowchartDescriptor()),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>(),
            childSlots:
            [
                new ExecutableChildSlot(
                    FlowchartActivity.ActivitiesSlotName,
                    children)
            ],
            structure: new ExecutableActivityStructure(
                FlowchartActivity.StructureKind,
                FlowchartActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new FlowchartStructure(connections))));

        return new WorkflowExecutable(
            identity: NewIdentity(),
            rootActivity: root,
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: _now,
            publishedAt: _now,
            compatibilityMetadata: new Dictionary<string, string>());
    }

    private static ExecutableNode NewProbeNode(string nodeId, IReadOnlyCollection<string>? outcomes = null) =>
        new(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: "test/probe",
            activityTypeVersion: "1.0.0",
            descriptorType: ProbeActivityConstructor.DescriptorTypeKey,
            descriptorPayload: JsonSerializer.SerializeToElement(new ProbeDescriptor(outcomes ?? [ActivityOutcomes.Done])),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>());

    private static FlowchartConnection NewConnection(string sourceNodeId, string targetNodeId, string? sourcePort = null) =>
        new(new FlowchartEndpoint(sourceNodeId, sourcePort), new FlowchartEndpoint(targetNodeId));

    private WorkflowExecutionActorActivationRequest NewActivationRequest(string workflowExecutionId) =>
        new(
            workflowExecutionId: workflowExecutionId,
            reason: WorkflowExecutionActorActivationReason.Start,
            requestedAt: _now,
            requestedBy: "flowchart-test",
            requiredCapabilities: WorkflowExecutionActorCapabilities.InProcessMailbox);

    private WorkflowExecutionCommandEnvelope NewStartEnvelope(WorkflowExecutableIdentity pinnedExecutable)
    {
        var payload = new WorkflowExecutionStartCommandPayload(pinnedExecutable, pinnedExecutable.ArtifactId);
        var command = new WorkflowExecutionCommand(
            CommandId: "command-start",
            WorkflowExecutionId: "wfexec-1",
            Kind: WorkflowExecutionCommandKind.Start,
            EnqueuedAt: _now,
            Payload: JsonSerializer.SerializeToElement(payload),
            Metadata: new Dictionary<string, string>());

        return new WorkflowExecutionCommandEnvelope(
            envelopeId: "envelope-start",
            workflowExecutionId: "wfexec-1",
            command: command,
            idempotencyKey: "wfexec-1:start:artifact-1",
            deliveryMode: WorkflowExecutionCommandDeliveryMode.AtLeastOnce,
            enqueuedAt: _now,
            sequence: 1,
            metadata: new Dictionary<string, string>());
    }

    private static WorkflowExecutableIdentity NewIdentity() =>
        new("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test");

    private sealed class FlowchartActivityConstructor : IActivityConstructor<FlowchartDescriptor>
    {
        public static string DescriptorTypeKey => typeof(FlowchartDescriptor).FullName!;
        public string DescriptorType => DescriptorTypeKey;

        public ValueTask<IActivity> Construct(
            JsonElement payload,
            IDictionary<string, InputArgument>? inputs,
            IDictionary<string, OutputArgument>? outputs,
            CancellationToken cancellationToken) =>
            new(new FlowchartActivity());

        public ValueTask<IActivity> Construct(
            FlowchartDescriptor descriptor,
            IDictionary<string, InputArgument>? inputs,
            IDictionary<string, OutputArgument>? outputs,
            CancellationToken cancellationToken) =>
            new(new FlowchartActivity());
    }

    private sealed record FlowchartDescriptor;

    private sealed class ProbeActivityConstructor : IActivityConstructor<ProbeDescriptor>
    {
        public static string DescriptorTypeKey => typeof(ProbeDescriptor).FullName!;
        public string DescriptorType => DescriptorTypeKey;

        public ValueTask<IActivity> Construct(
            JsonElement payload,
            IDictionary<string, InputArgument>? inputs,
            IDictionary<string, OutputArgument>? outputs,
            CancellationToken cancellationToken)
        {
            var descriptor = payload.Deserialize<ProbeDescriptor>()
                             ?? throw new InvalidOperationException("Probe descriptor resolved to null.");
            return Construct(descriptor, inputs, outputs, cancellationToken);
        }

        public ValueTask<IActivity> Construct(
            ProbeDescriptor descriptor,
            IDictionary<string, InputArgument>? inputs,
            IDictionary<string, OutputArgument>? outputs,
            CancellationToken cancellationToken) =>
            new(new ProbeActivity(descriptor.Outcomes));
    }

    private sealed record ProbeDescriptor(IReadOnlyCollection<string> Outcomes);

    private sealed class ProbeActivity(IReadOnlyCollection<string> outcomes) : CodeActivity("test/probe")
    {
        protected override void Execute(IActivityExecutionContext context) =>
            context.SetOutcomes(outcomes.ToArray());
    }

    private sealed class DeterministicRuntimeExecutionIdGenerator(IEnumerable<string> activityExecutionIds) : IRuntimeExecutionIdGenerator
    {
        private readonly Queue<string> _activityExecutionIds = new(activityExecutionIds);

        public string NewWorkflowExecutionId() => "wfexec-1";

        public string NewWorkflowExecutionCommandId() => "command-generated";

        public string NewWorkflowExecutionCommandEnvelopeId() => "envelope-generated";

        public string NewActivityExecutionId() =>
            _activityExecutionIds.TryDequeue(out var activityExecutionId)
                ? activityExecutionId
                : throw new InvalidOperationException("No deterministic activity execution ID is available.");
    }
}
