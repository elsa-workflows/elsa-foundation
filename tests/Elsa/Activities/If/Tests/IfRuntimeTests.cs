using System.Text.Json;
using Elsa.Activities.If;
using Elsa.Activities.Runtime;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using IfActivity = Elsa.Activities.If.Activities.If;

namespace Elsa.Activities.If.Tests;

/// <summary>
/// In-process execution coverage for the <c>If</c> composite running through the real workflow agent
/// (the FlowchartRuntimeFixture pattern). Asserts the matching branch runs, the other does not, and the
/// composite emits the True/False outcome.
/// </summary>
public sealed class IfRuntimeTests
{
    private readonly DateTimeOffset _now = new(2026, 6, 12, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task TrueCondition_RunsThenBranch_AndEmitsTrueOutcome()
    {
        await using var provider = NewProvider(["actexec-if", "actexec-then"]);
        var executable = NewExecutable(condition: true);

        await ExecuteAsync(provider, executable);

        var states = await provider.GetRequiredService<IActivityExecutionStateStore>().ListAsync("wfexec-1");
        var ifState = Assert.Single(states, state => state.Execution.ExecutableNodeId == "node-if");
        Assert.Equal(ActivityExecutionStatus.Completed, ifState.Status);
        Assert.Equal([ActivityOutcomes.True], CompletionOutcomes(ifState));
        Assert.Contains(states, state => state.Execution.ExecutableNodeId == "node-then");
        Assert.DoesNotContain(states, state => state.Execution.ExecutableNodeId == "node-else");
    }

    [Fact]
    public async Task FalseCondition_RunsElseBranch_AndEmitsFalseOutcome()
    {
        await using var provider = NewProvider(["actexec-if", "actexec-else"]);
        var executable = NewExecutable(condition: false);

        await ExecuteAsync(provider, executable);

        var states = await provider.GetRequiredService<IActivityExecutionStateStore>().ListAsync("wfexec-1");
        var ifState = Assert.Single(states, state => state.Execution.ExecutableNodeId == "node-if");
        Assert.Equal(ActivityExecutionStatus.Completed, ifState.Status);
        Assert.Equal([ActivityOutcomes.False], CompletionOutcomes(ifState));
        Assert.Contains(states, state => state.Execution.ExecutableNodeId == "node-else");
        Assert.DoesNotContain(states, state => state.Execution.ExecutableNodeId == "node-then");
    }

    [Fact]
    public async Task TerminalBranch_CompletesWorkflow()
    {
        await using var provider = NewProvider(["actexec-if", "actexec-then"]);
        var executable = NewExecutable(condition: true);

        await ExecuteAsync(provider, executable);

        var workflowState = await provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("wfexec-1");
        Assert.Equal(WorkflowExecutionStatus.Completed, workflowState?.Status);
    }

    [Fact]
    public async Task SelectedBranchIsEmpty_FinalizesWithoutSchedulingChild_AndCompletesWorkflow()
    {
        // Condition selects the Then branch, but its slot is empty: the composite must finalize via
        // CompleteCompositeActivity (True outcome) with no child scheduled, and the run must complete.
        await using var provider = NewProvider(["actexec-if"]);
        var executable = NewExecutable(condition: true, includeThen: false);

        await ExecuteAsync(provider, executable);

        var states = await provider.GetRequiredService<IActivityExecutionStateStore>().ListAsync("wfexec-1");
        var ifState = Assert.Single(states, state => state.Execution.ExecutableNodeId == "node-if");
        Assert.Equal(ActivityExecutionStatus.Completed, ifState.Status);
        Assert.Equal([ActivityOutcomes.True], CompletionOutcomes(ifState));
        Assert.DoesNotContain(states, state => state.Execution.ExecutableNodeId == "node-then");
        Assert.DoesNotContain(states, state => state.Execution.ExecutableNodeId == "node-else");

        var workflowState = await provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("wfexec-1");
        Assert.Equal(WorkflowExecutionStatus.Completed, workflowState?.Status);
    }

    private ServiceProvider NewProvider(IEnumerable<string> activityExecutionIds)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IActivityConstructor, IfActivityConstructor>();
        services.AddSingleton<IActivityConstructor, ProbeActivityConstructor>();
        services.AddSingleton<IRuntimeExecutionIdGenerator>(new DeterministicRuntimeExecutionIdGenerator(activityExecutionIds));
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);
        new ActivitiesRuntimeFeature().ConfigureServices(services);
        new ActivitiesIfFeature().ConfigureServices(services);

        return services.BuildServiceProvider();
    }

    private async Task ExecuteAsync(ServiceProvider provider, WorkflowExecutable executable)
    {
        await provider.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(executable);
        var agent = await provider.GetRequiredService<IWorkflowExecutionAgentProvider>()
            .GetAgentAsync(NewActivationRequest("wfexec-1"));

        var result = await agent.EnqueueAsync(NewStartEnvelope(executable.Identity));

        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted, result.Status);
        Assert.Empty(await provider.GetRequiredService<IWorkflowSchedulerWorkQueue>().ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
    }

    private WorkflowExecutable NewExecutable(bool condition, bool includeThen = true, bool includeElse = true)
    {
        var childSlots = new List<ExecutableChildSlot>();
        if (includeThen)
            childSlots.Add(new ExecutableChildSlot(IfActivity.ThenSlotName, [NewProbeNode("node-then")]));
        if (includeElse)
            childSlots.Add(new ExecutableChildSlot(IfActivity.ElseSlotName, [NewProbeNode("node-else")]));

        var root = new ExecutableNode(
            executableNodeId: "node-if",
            authoredActivityId: "authored-if",
            activityType: typeof(IfActivity).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: IfActivityConstructor.DescriptorTypeKey,
            descriptorPayload: JsonSerializer.SerializeToElement(new IfDescriptor()),
            inputBindings: new Dictionary<string, RuntimeInputBinding>
            {
                ["Condition"] = new RuntimeInputBinding(
                    inputName: "Condition",
                    source: RuntimeInputBindingSource.Literal,
                    literalValue: JsonSerializer.SerializeToElement(condition),
                    metadata: new Dictionary<string, string> { [RuntimeActivityInputMaterializer.InputTypeMetadataKey] = "System.Boolean" })
            },
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>(),
            childSlots: childSlots,
            structure: new ExecutableActivityStructure(
                IfActivity.StructureKind,
                IfActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new
                {
                    then = includeThen ? "node-then" : null,
                    @else = includeElse ? "node-else" : null
                })));

        return new WorkflowExecutable(
            identity: NewIdentity(),
            rootActivity: root,
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: _now,
            publishedAt: _now,
            compatibilityMetadata: new Dictionary<string, string>());
    }

    private static IReadOnlyCollection<string> CompletionOutcomes(ActivityExecutionState state) =>
        state.Metadata.TryGetValue(RuntimeMetadataKeys.CompletionOutcomeNames, out var serialized)
            ? JsonSerializer.Deserialize<string[]>(serialized) ?? []
            : [];

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

    private WorkflowExecutionAgentActivationRequest NewActivationRequest(string workflowExecutionId) =>
        new(
            workflowExecutionId: workflowExecutionId,
            reason: WorkflowExecutionAgentActivationReason.Start,
            requestedAt: _now,
            requestedBy: "if-test",
            requiredCapabilities: WorkflowExecutionAgentCapabilities.InProcessMailbox);

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

    private sealed class IfActivityConstructor : IActivityConstructor<IfDescriptor>
    {
        public static string DescriptorTypeKey => typeof(IfDescriptor).FullName!;
        public string DescriptorType => DescriptorTypeKey;

        public ValueTask<IActivity> Construct(
            JsonElement payload,
            IDictionary<string, InputArgument>? inputs,
            IDictionary<string, OutputArgument>? outputs,
            CancellationToken cancellationToken) =>
            Construct(new IfDescriptor(), inputs, outputs, cancellationToken);

        public ValueTask<IActivity> Construct(
            IfDescriptor descriptor,
            IDictionary<string, InputArgument>? inputs,
            IDictionary<string, OutputArgument>? outputs,
            CancellationToken cancellationToken)
        {
            var activity = new IfActivity();
            if (inputs is not null && inputs.TryGetValue("Condition", out var conditionInput))
                activity.Condition = (InputArgument<bool>)conditionInput;
            return new(activity);
        }
    }

    private sealed record IfDescriptor;

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
