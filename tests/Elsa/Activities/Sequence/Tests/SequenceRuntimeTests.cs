using System.Text.Json;
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
using SequenceActivity = Elsa.Activities.Sequence.Activities.Sequence;

namespace Elsa.Activities.Sequence.Tests;

public sealed class SequenceRuntimeTests
{
    private readonly DateTimeOffset _now = new(2026, 6, 12, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task SequenceRoot_SchedulesFirstChildAndPersistsParentExecutionId()
    {
        await using var provider = NewProvider(["actexec-sequence", "actexec-a", "actexec-b", "actexec-c"]);
        var executable = NewExecutable([NewProbeNode("node-a"), NewProbeNode("node-b"), NewProbeNode("node-c")]);

        await ExecuteAsync(provider, executable);

        var states = await provider.GetRequiredService<IActivityExecutionStateStore>().ListAsync("wfexec-1");
        var sequenceState = Assert.Single(states, state => state.Execution.ExecutableNodeId == "node-sequence");
        var firstChildState = Assert.Single(states, state => state.Execution.ExecutableNodeId == "node-a");
        Assert.Equal(ActivityExecutionStatus.Completed, sequenceState.Status);
        Assert.Equal("actexec-sequence", firstChildState.ParentActivityExecutionId);
        Assert.Equal("actexec-sequence", firstChildState.SchedulingActivityExecutionId);
    }

    [Fact]
    public async Task CompletedChild_SchedulesNextChildInStructureOrder()
    {
        await using var provider = NewProvider(["actexec-sequence", "actexec-a", "actexec-b", "actexec-c"]);
        var executable = NewExecutable([NewProbeNode("node-a"), NewProbeNode("node-b"), NewProbeNode("node-c")]);

        await ExecuteAsync(provider, executable);

        var states = await provider.GetRequiredService<IActivityExecutionStateStore>().ListAsync("wfexec-1");
        var secondChildState = Assert.Single(states, state => state.Execution.ExecutableNodeId == "node-b");
        var thirdChildState = Assert.Single(states, state => state.Execution.ExecutableNodeId == "node-c");
        Assert.Equal(ActivityExecutionStatus.Completed, secondChildState.Status);
        Assert.Equal(ActivityExecutionStatus.Completed, thirdChildState.Status);
        Assert.Equal("actexec-sequence", secondChildState.ParentActivityExecutionId);
        Assert.Equal("actexec-sequence", thirdChildState.ParentActivityExecutionId);
    }

    [Fact]
    public async Task SchedulingActivityExecutionId_ChainsFromPreviousActivityExecution()
    {
        await using var provider = NewProvider(["actexec-sequence", "actexec-a", "actexec-b", "actexec-c"]);
        var executable = NewExecutable([NewProbeNode("node-a"), NewProbeNode("node-b"), NewProbeNode("node-c")]);

        await ExecuteAsync(provider, executable);

        var states = await provider.GetRequiredService<IActivityExecutionStateStore>().ListAsync("wfexec-1");
        var firstChildState = Assert.Single(states, state => state.Execution.ExecutableNodeId == "node-a");
        var secondChildState = Assert.Single(states, state => state.Execution.ExecutableNodeId == "node-b");
        var thirdChildState = Assert.Single(states, state => state.Execution.ExecutableNodeId == "node-c");
        Assert.Equal("actexec-sequence", firstChildState.SchedulingActivityExecutionId);
        Assert.Equal("actexec-a", secondChildState.SchedulingActivityExecutionId);
        Assert.Equal("actexec-b", thirdChildState.SchedulingActivityExecutionId);
    }

    [Fact]
    public async Task FinalChildCompletion_CompletesSequenceAndWorkflow()
    {
        await using var provider = NewProvider(["actexec-sequence", "actexec-a", "actexec-b", "actexec-c"]);
        var executable = NewExecutable([NewProbeNode("node-a"), NewProbeNode("node-b"), NewProbeNode("node-c")]);

        await ExecuteAsync(provider, executable);

        var workflowState = await provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("wfexec-1");
        var states = await provider.GetRequiredService<IActivityExecutionStateStore>().ListAsync("wfexec-1");
        var sequenceState = Assert.Single(states, state => state.Execution.ExecutableNodeId == "node-sequence");
        Assert.Equal(ActivityExecutionStatus.Completed, sequenceState.Status);
        Assert.Equal(WorkflowExecutionStatus.Completed, workflowState?.Status);
    }

    private ServiceProvider NewProvider(IEnumerable<string> activityExecutionIds)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IActivityConstructor, SequenceActivityConstructor>();
        services.AddSingleton<IActivityConstructor, ProbeActivityConstructor>();
        services.AddSingleton<IRuntimeExecutionIdGenerator>(new DeterministicRuntimeExecutionIdGenerator(activityExecutionIds));
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);
        new ActivitiesRuntimeFeature().ConfigureServices(services);
        new ActivitiesSequenceFeature().ConfigureServices(services);

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

    private WorkflowExecutable NewExecutable(IReadOnlyCollection<ExecutableNode> children)
    {
        var root = new ExecutableNode(
            executableNodeId: "node-sequence",
            authoredActivityId: "authored-sequence",
            activityType: typeof(SequenceActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: SequenceActivityConstructor.DescriptorTypeKey,
            descriptorPayload: JsonSerializer.SerializeToElement(new SequenceDescriptor()),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>(),
            childSlots:
            [
                new ExecutableChildSlot(
                    SequenceActivity.ActivitiesSlotName,
                    children)
            ],
            structure: NewSequenceStructure(children.Select(child => child.ExecutableNodeId).ToArray()));

        return new WorkflowExecutable(
            identity: NewIdentity(),
            rootActivity: root,
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: _now,
            publishedAt: _now,
            compatibilityMetadata: new Dictionary<string, string>());
    }

    private static ExecutableNode NewProbeNode(string nodeId) =>
        new(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: "test/probe",
            activityTypeVersion: "1.0.0",
            descriptorType: ProbeActivityConstructor.DescriptorTypeKey,
            descriptorPayload: JsonSerializer.SerializeToElement(new ProbeDescriptor([ActivityOutcomes.Done])),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>());

    private WorkflowExecutionActorActivationRequest NewActivationRequest(string workflowExecutionId) =>
        new(
            workflowExecutionId: workflowExecutionId,
            reason: WorkflowExecutionActorActivationReason.Start,
            requestedAt: _now,
            requestedBy: "sequence-test",
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

    private static ExecutableActivityStructure NewSequenceStructure(IReadOnlyCollection<string> activities) =>
        new(
            SequenceActivity.StructureKind,
            SequenceActivity.StructureSchemaVersion,
            JsonSerializer.SerializeToElement(new { activities }));

    private sealed class SequenceActivityConstructor : IActivityConstructor<SequenceDescriptor>
    {
        public static string DescriptorTypeKey => typeof(SequenceDescriptor).FullName!;
        public string DescriptorType => DescriptorTypeKey;

        public ValueTask<IActivity> Construct(
            JsonElement payload,
            IDictionary<string, InputArgument>? inputs,
            IDictionary<string, OutputArgument>? outputs,
            CancellationToken cancellationToken) =>
            new(new SequenceActivity());

        public ValueTask<IActivity> Construct(
            SequenceDescriptor descriptor,
            IDictionary<string, InputArgument>? inputs,
            IDictionary<string, OutputArgument>? outputs,
            CancellationToken cancellationToken) =>
            new(new SequenceActivity());
    }

    private sealed record SequenceDescriptor;

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
