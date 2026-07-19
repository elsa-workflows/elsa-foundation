using System.Text.Json;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Runtime;
using Elsa.Activities.Runtime.Contracts;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// End-to-end guard for the <c>Fault</c> leaf activity: running a workflow whose only activity is a
/// <c>Fault</c> must record a blocking incident through the engine incident model rather than throwing
/// out to the host. The agent accepts the command, the run does not surface the exception to the caller,
/// the activity state is Faulted, and an <c>IncidentState</c> is persisted for inspection/intervention.
/// </summary>
public sealed class FaultIncidentExecutionTests
{
    private readonly DateTimeOffset _now = new(2026, 6, 12, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task FaultActivity_RecordsBlockingIncident_WithoutThrowingToHost()
    {
        await using var provider = NewProvider(["actexec-fault"]);
        var executable = NewExecutable("Boom!");

        // The agent must accept and drain without fault control flow propagating to the caller.
        await ExecuteAsync(provider, executable);

        var states = await provider.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync("wfexec-1");
        var faultState = Assert.Single(states, state => state.Execution.ExecutableNodeId == "node-fault");
        Assert.Equal(ActivityExecutionStatus.Faulted, faultState.Status);
        Assert.Equal("ActivityReturnedFault", faultState.SubStatus);
        Assert.Equal("workflow.fault", faultState.Fault!.Code);

        var incidents = await provider.GetRequiredService<IIncidentStateStore>().ListBlockingAsync("wfexec-1");
        var incident = Assert.Single(incidents);
        Assert.True(incident.IsBlocking);
        Assert.Equal(IncidentStatus.Blocking, incident.Status);
        Assert.Equal(IncidentSeverity.Error, incident.Severity);
        Assert.Equal("Boom!", incident.Message);
        Assert.Equal("node-fault", incident.ExecutableNodeId);

        // RT-1 acceptance: the blocking incident drives the workflow out of Running to a queryable Faulted status
        // (the fault observer commits a WorkflowFaulted checkpoint post-drain).
        var workflowState = await provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("wfexec-1");
        Assert.NotNull(workflowState);
        Assert.Equal(WorkflowExecutionStatus.Faulted, workflowState!.Status);
    }

    [Fact]
    public async Task FaultActivity_UsesDefaultMessage_WhenNoMessageIsBound()
    {
        await using var provider = NewProvider(["actexec-fault"]);
        var executable = NewExecutable(message: null);

        await ExecuteAsync(provider, executable);

        var incidents = await provider.GetRequiredService<IIncidentStateStore>().ListBlockingAsync("wfexec-1");
        var incident = Assert.Single(incidents);
        Assert.Equal("The workflow faulted.", incident.Message);
    }

    private ServiceProvider NewProvider(IEnumerable<string> activityExecutionIds)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IRuntimeExecutionIdGenerator>(new DeterministicRuntimeExecutionIdGenerator(activityExecutionIds));
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);
        new ActivitiesRuntimeFeature().ConfigureServices(services);
        services.AddSingleton<IActivityActivator, FaultActivityActivator>();

        return services.BuildServiceProvider();
    }

    private async Task ExecuteAsync(ServiceProvider provider, WorkflowExecutable executable)
    {
        await provider.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(executable);
        var agent = await provider.GetRequiredService<IWorkflowExecutionActorProvider>()
            .GetAgentAsync(NewActivationRequest("wfexec-1"));

        var result = await agent.EnqueueAsync(NewStartEnvelope(executable.Identity));

        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted, result.Status);
        Assert.Empty(await provider.GetRequiredService<IWorkflowSchedulerWorkQueue>().ListAllAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
    }

    private WorkflowExecutable NewExecutable(string? message)
    {
        var stringType = new ValueTypeDescriptor("String");
        var inputBindings = new Dictionary<string, RuntimeInputBinding>
        {
            ["message"] = new RuntimeInputBinding(
                inputKey: "message",
                targetType: stringType,
                effectivePolicy: ValueProtectionPolicy.InstanceInline,
                source: RuntimeInputBindingSource.Literal,
                literal: message is null
                    ? ValueEnvelope.Null(stringType, ValueProtectionPolicy.InstanceInline)
                    : ValueEnvelope.Inline(stringType, JsonSerializer.SerializeToElement(message), ValueProtectionPolicy.InstanceInline))
        };
        using var descriptor = JsonDocument.Parse("""{"type":"fault"}""");
        var contract = new ActivityContract(
            typeof(Fault).FullName!,
            "1.0.0",
            "test/fault",
            descriptor.RootElement,
            [new ActivityInputContract("message", nameof(Fault.Message), stringType, false, true, false, null, ActivityValuePolicy.Default)],
            new ActivityResultContract(new ValueTypeDescriptor("Elsa.Unit"), true, ActivityValuePolicy.Default, []),
            [ActivityOutcomes.Done],
            new ActivityActivationRequirement("test/fault", typeof(Fault).FullName!));

        var root = new ExecutableNode(
            executableNodeId: "node-fault",
            authoredActivityId: "authored-fault",
            activityType: typeof(Fault).FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: "test/fault",
            descriptorPayload: descriptor.RootElement,
            inputBindings: inputBindings,
            metadata: new Dictionary<string, string>(),
            activityContract: contract);

        return new WorkflowExecutable(
            identity: NewIdentity(),
            rootActivity: root,
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: _now,
            compatibilityMetadata: new Dictionary<string, string>());
    }

    private WorkflowExecutionActorActivationRequest NewActivationRequest(string workflowExecutionId) =>
        new(
            workflowExecutionId: workflowExecutionId,
            reason: WorkflowExecutionActorActivationReason.Start,
            requestedAt: _now,
            requestedBy: "fault-test",
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

    private sealed class FaultActivityActivator : IActivityActivator
    {
        public ValueTask<ActivityActivationLease> ActivateAsync(
            ActivityActivationRequest request,
            CancellationToken cancellationToken = default)
        {
            var value = request.Inputs.Values["message"];
            var activity = new Fault
            {
                Message = value.Presence == ValuePresence.ExplicitNull
                    ? null
                    : value.InlineValue!.Value.GetString()
            };
            return ValueTask.FromResult(new ActivityActivationLease(activity));
        }
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
