using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeAgentProviderContractTests
{
    private readonly DateTimeOffset _now = new(2026, 6, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void CommandEnvelope_CarriesIdempotencyAndSequenceForAgentDelivery()
    {
        var command = NewCommand(WorkflowExecutionCommandKind.ContinueVolatileWait);
        var envelope = new WorkflowExecutionCommandEnvelope(
            envelopeId: "envelope-1",
            workflowExecutionId: "wfexec-1",
            command: command,
            idempotencyKey: "wfexec-1:command-1",
            deliveryMode: WorkflowExecutionCommandDeliveryMode.AtLeastOnce,
            enqueuedAt: _now,
            sequence: 42,
            metadata: new Dictionary<string, string> { ["provider"] = "in-process" });

        Assert.Equal("wfexec-1", envelope.WorkflowExecutionId);
        Assert.Same(command, envelope.Command);
        Assert.Equal("wfexec-1:command-1", envelope.IdempotencyKey);
        Assert.Equal(42, envelope.Sequence);
        Assert.Equal(WorkflowExecutionCommandDeliveryMode.AtLeastOnce, envelope.DeliveryMode);
        Assert.Equal("in-process", envelope.Metadata["provider"]);
    }

    [Fact]
    public void CommandEnvelope_RejectsInvalidDeliveryMetadata()
    {
        Assert.Throws<ArgumentException>(() => new WorkflowExecutionCommandEnvelope(
            envelopeId: "envelope-1",
            workflowExecutionId: "wfexec-1",
            command: NewCommand(),
            idempotencyKey: " ",
            deliveryMode: WorkflowExecutionCommandDeliveryMode.AtLeastOnce,
            enqueuedAt: _now));

        Assert.Throws<ArgumentOutOfRangeException>(() => new WorkflowExecutionCommandEnvelope(
            envelopeId: "envelope-1",
            workflowExecutionId: "wfexec-1",
            command: NewCommand(),
            idempotencyKey: "wfexec-1:command-1",
            deliveryMode: WorkflowExecutionCommandDeliveryMode.AtLeastOnce,
            enqueuedAt: _now,
            sequence: -1));

        Assert.Throws<ArgumentException>(() => new WorkflowExecutionCommandEnvelope(
            envelopeId: "envelope-1",
            workflowExecutionId: "wfexec-2",
            command: NewCommand(),
            idempotencyKey: "wfexec-1:command-1",
            deliveryMode: WorkflowExecutionCommandDeliveryMode.AtLeastOnce,
            enqueuedAt: _now));
    }

    [Fact]
    public void AgentProviderContract_UsesActivationRequestAndProviderCapabilities()
    {
        Assert.True(typeof(IWorkflowExecutionAgent).IsInterface);
        Assert.True(typeof(IWorkflowExecutionAgentProvider).IsInterface);

        var providerMethod = typeof(IWorkflowExecutionAgentProvider).GetMethod(nameof(IWorkflowExecutionAgentProvider.GetAgentAsync))!;
        var parameters = providerMethod.GetParameters();
        var passivateMethod = typeof(IWorkflowExecutionAgentProvider).GetMethod(nameof(IWorkflowExecutionAgentProvider.PassivateAsync))!;
        var passivateParameters = passivateMethod.GetParameters();

        Assert.Equal(typeof(WorkflowExecutionAgentActivationRequest), parameters[0].ParameterType);
        Assert.Equal(typeof(WorkflowExecutionAgentPassivationRequest), passivateParameters[0].ParameterType);
        Assert.Equal(typeof(WorkflowExecutionAgentCapabilities), typeof(IWorkflowExecutionAgentProvider).GetProperty(nameof(IWorkflowExecutionAgentProvider.Capabilities))!.PropertyType);
    }

    [Fact]
    public void AgentContract_EnqueuesCommandEnvelopesAndReturnsDispatchResult()
    {
        var method = typeof(IWorkflowExecutionAgent).GetMethod(nameof(IWorkflowExecutionAgent.EnqueueAsync))!;
        var parameters = method.GetParameters();

        Assert.Equal(typeof(ValueTask<WorkflowExecutionCommandDispatchResult>), method.ReturnType);
        Assert.Equal(typeof(WorkflowExecutionCommandEnvelope), parameters[0].ParameterType);
        Assert.Equal(typeof(WorkflowExecutionAgentDescriptor), typeof(IWorkflowExecutionAgent).GetProperty(nameof(IWorkflowExecutionAgent.Descriptor))!.PropertyType);
    }

    [Fact]
    public void CommandKinds_IncludeActorStyleAgentVocabulary()
    {
        var names = Enum.GetNames<WorkflowExecutionCommandKind>();

        Assert.Contains(nameof(WorkflowExecutionCommandKind.ScheduleActivity), names);
        Assert.Contains(nameof(WorkflowExecutionCommandKind.CompleteActivity), names);
        Assert.Contains(nameof(WorkflowExecutionCommandKind.DeliverSignal), names);
        Assert.Contains(nameof(WorkflowExecutionCommandKind.CreateBookmark), names);
        Assert.Contains(nameof(WorkflowExecutionCommandKind.Checkpoint), names);
    }

    [Fact]
    public void AgentDescriptor_IsFrameworkNeutralAndKeepsCheckpointStateAuthoritative()
    {
        var descriptor = new WorkflowExecutionAgentDescriptor(
            workflowExecutionId: "wfexec-1",
            agentId: "agent-1",
            providerName: "InProcessMailboxProvider",
            status: WorkflowExecutionAgentStatus.Active,
            capabilities: WorkflowExecutionAgentCapabilities.InProcessMailbox | WorkflowExecutionAgentCapabilities.Passivation,
            activatedAt: _now,
            lastCheckpointId: "checkpoint-1");

        Assert.Equal("wfexec-1", descriptor.WorkflowExecutionId);
        Assert.Equal("checkpoint-1", descriptor.LastCheckpointId);
        Assert.True(descriptor.Capabilities.HasFlag(WorkflowExecutionAgentCapabilities.InProcessMailbox));
        Assert.DoesNotContain(
            typeof(WorkflowExecutionAgentDescriptor).GetProperties().Select(property => property.PropertyType.ToString()),
            typeName => typeName.Contains("Orleans", StringComparison.OrdinalIgnoreCase)
                || typeName.Contains("Dapr", StringComparison.OrdinalIgnoreCase)
                || typeName.Contains("ProtoActor", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ActivationRequest_IsKeyedByWorkflowExecutionIdAndReason()
    {
        var request = new WorkflowExecutionAgentActivationRequest(
            workflowExecutionId: "wfexec-1",
            reason: WorkflowExecutionAgentActivationReason.ResumeBookmark,
            requestedAt: _now,
            requestedBy: "dispatcher-1",
            requiredCapabilities: WorkflowExecutionAgentCapabilities.InProcessMailbox | WorkflowExecutionAgentCapabilities.LeaseFencing);

        Assert.Equal("wfexec-1", request.WorkflowExecutionId);
        Assert.Equal(WorkflowExecutionAgentActivationReason.ResumeBookmark, request.Reason);
        Assert.True(request.RequiredCapabilities.HasFlag(WorkflowExecutionAgentCapabilities.LeaseFencing));
    }

    [Fact]
    public void PassivationRequest_NamesSafeBoundary()
    {
        var request = new WorkflowExecutionAgentPassivationRequest(
            workflowExecutionId: "wfexec-1",
            boundary: WorkflowExecutionAgentPassivationBoundary.AfterCheckpointCommit,
            requestedAt: _now,
            reason: "Host drain");

        Assert.Equal("wfexec-1", request.WorkflowExecutionId);
        Assert.Equal(WorkflowExecutionAgentPassivationBoundary.AfterCheckpointCommit, request.Boundary);
        Assert.DoesNotContain(
            Enum.GetNames<WorkflowExecutionAgentPassivationBoundary>(),
            name => name.Contains("Mid", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DispatchResult_AllowsReasonsForNonAcceptedOutcomes()
    {
        var accepted = new WorkflowExecutionCommandDispatchResult(
            envelopeId: "envelope-1",
            workflowExecutionId: "wfexec-1",
            status: WorkflowExecutionCommandDispatchStatus.Accepted,
            recordedAt: _now);

        var duplicate = new WorkflowExecutionCommandDispatchResult(
            envelopeId: "envelope-duplicate",
            workflowExecutionId: "wfexec-1",
            status: WorkflowExecutionCommandDispatchStatus.Duplicate,
            recordedAt: _now,
            reason: "Idempotency key already processed");

        var rejected = new WorkflowExecutionCommandDispatchResult(
            envelopeId: "envelope-2",
            workflowExecutionId: "wfexec-1",
            status: WorkflowExecutionCommandDispatchStatus.Rejected,
            recordedAt: _now,
            reason: "Workflow execution paused");

        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted, accepted.Status);
        Assert.Equal("Idempotency key already processed", duplicate.Reason);
        Assert.Equal("Workflow execution paused", rejected.Reason);
        Assert.ThrowsAny<ArgumentException>(() => new WorkflowExecutionCommandDispatchResult(
            envelopeId: "envelope-3",
            workflowExecutionId: "wfexec-1",
            status: WorkflowExecutionCommandDispatchStatus.Rejected,
            recordedAt: _now));
        Assert.ThrowsAny<ArgumentException>(() => new WorkflowExecutionCommandDispatchResult(
            envelopeId: "envelope-3b",
            workflowExecutionId: "wfexec-1",
            status: WorkflowExecutionCommandDispatchStatus.Deferred,
            recordedAt: _now));
        Assert.Throws<ArgumentException>(() => new WorkflowExecutionCommandDispatchResult(
            envelopeId: "envelope-4",
            workflowExecutionId: "wfexec-1",
            status: WorkflowExecutionCommandDispatchStatus.Accepted,
            recordedAt: _now,
            reason: "No reason allowed"));
    }

    [Fact]
    public void AgentContracts_DoNotIntroduceActorFrameworkDependencies()
    {
        var runtimeCoreAssembly = typeof(IWorkflowExecutionAgentProvider).Assembly;
        var referencedAssemblies = runtimeCoreAssembly.GetReferencedAssemblies().Select(assembly => assembly.Name).ToArray();

        Assert.DoesNotContain(referencedAssemblies, name => name!.Contains("Orleans", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(referencedAssemblies, name => name!.Contains("Dapr", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(referencedAssemblies, name => name!.Contains("Proto", StringComparison.OrdinalIgnoreCase));
    }

    private WorkflowExecutionCommand NewCommand(WorkflowExecutionCommandKind kind = WorkflowExecutionCommandKind.RunSchedulerWork)
    {
        using var document = JsonDocument.Parse("""{"workItemId":"work-1"}""");
        return new(
            CommandId: "command-1",
            WorkflowExecutionId: "wfexec-1",
            Kind: kind,
            EnqueuedAt: _now,
            Payload: document.RootElement.Clone(),
            Metadata: new Dictionary<string, string>());
    }
}
