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
        Assert.True(typeof(IWorkflowExecutionActor).IsInterface);
        Assert.True(typeof(IWorkflowExecutionActorProvider).IsInterface);

        var providerMethod = typeof(IWorkflowExecutionActorProvider).GetMethod(nameof(IWorkflowExecutionActorProvider.GetAgentAsync))!;
        var parameters = providerMethod.GetParameters();
        var passivateMethod = typeof(IWorkflowExecutionActorProvider).GetMethod(nameof(IWorkflowExecutionActorProvider.PassivateAsync))!;
        var passivateParameters = passivateMethod.GetParameters();

        Assert.Equal(typeof(WorkflowExecutionActorActivationRequest), parameters[0].ParameterType);
        Assert.Equal(typeof(WorkflowExecutionActorPassivationRequest), passivateParameters[0].ParameterType);
        Assert.Equal(typeof(WorkflowExecutionActorCapabilities), typeof(IWorkflowExecutionActorProvider).GetProperty(nameof(IWorkflowExecutionActorProvider.Capabilities))!.PropertyType);
    }

    [Fact]
    public void RuntimeCore_DoesNotExposeLegacyWorkflowExecutionPool()
    {
        var runtimeCoreAssembly = typeof(IWorkflowExecutionActorProvider).Assembly;

        Assert.Null(runtimeCoreAssembly.GetType("Elsa.Workflows.Runtime.Core.Contracts.IWorkflowExecutionPool"));
        Assert.NotNull(runtimeCoreAssembly.GetType("Elsa.Workflows.Runtime.Core.Contracts.IWorkflowExecutionActorProvider"));
    }

    [Fact]
    public void RuntimeCore_DoesNotExposeDirectWorkflowExecutor()
    {
        var runtimeCoreAssembly = typeof(IWorkflowExecutionActorProvider).Assembly;

        Assert.Null(runtimeCoreAssembly.GetType("Elsa.Workflows.Runtime.Core.Contracts.IWorkflowExecutor"));
        Assert.Null(runtimeCoreAssembly.GetType("Elsa.Workflows.Runtime.Core.Services.SequentialWorkflowExecutor"));
        Assert.NotNull(runtimeCoreAssembly.GetType("Elsa.Workflows.Runtime.Core.Contracts.IWorkflowStartDispatcher"));
        Assert.NotNull(runtimeCoreAssembly.GetType("Elsa.Workflows.Runtime.Core.Contracts.IWorkflowExecutionActorProvider"));
    }

    [Fact]
    public void AgentContract_EnqueuesCommandEnvelopesAndReturnsDispatchResult()
    {
        var defaultMethod = typeof(IWorkflowExecutionActor).GetMethod(
            nameof(IWorkflowExecutionActor.EnqueueAsync),
            [typeof(WorkflowExecutionCommandEnvelope), typeof(CancellationToken)])!;
        var optionsMethod = typeof(IWorkflowExecutionActor).GetMethod(
            nameof(IWorkflowExecutionActor.EnqueueAsync),
            [typeof(WorkflowExecutionCommandEnvelope), typeof(WorkflowExecutionCommandDispatchOptions), typeof(CancellationToken)])!;
        var parameters = defaultMethod.GetParameters();
        var optionsParameters = optionsMethod.GetParameters();

        Assert.Equal(typeof(ValueTask<WorkflowExecutionCommandDispatchResult>), defaultMethod.ReturnType);
        Assert.Equal(typeof(ValueTask<WorkflowExecutionCommandDispatchResult>), optionsMethod.ReturnType);
        Assert.Equal(typeof(WorkflowExecutionCommandEnvelope), parameters[0].ParameterType);
        Assert.Equal(typeof(WorkflowExecutionCommandEnvelope), optionsParameters[0].ParameterType);
        Assert.Equal(typeof(WorkflowExecutionCommandDispatchOptions), optionsParameters[1].ParameterType);
        Assert.Equal(typeof(WorkflowExecutionActorDescriptor), typeof(IWorkflowExecutionActor).GetProperty(nameof(IWorkflowExecutionActor.Descriptor))!.PropertyType);
    }

    [Fact]
    public void CommandEnvelope_DoesNotCarryRequestAffineServices()
    {
        var envelopeProperties = typeof(WorkflowExecutionCommandEnvelope).GetProperties();

        Assert.DoesNotContain(envelopeProperties, property => typeof(IServiceProvider).IsAssignableFrom(property.PropertyType));
        Assert.Equal(typeof(IServiceProvider), typeof(WorkflowExecutionCommandDispatchOptions).GetProperty(nameof(WorkflowExecutionCommandDispatchOptions.AmbientServices))!.PropertyType);
    }

    [Fact]
    public void StimulusDispatchRequest_KeepsAmbientServicesOffTheDurableMetadataChannel()
    {
        // Spec 089 E-D4 / FR-021: the request carries request-affine ambient services ONLY on the dedicated
        // DispatchOptions channel (a live reference), never through the durable Metadata channel that flows into the
        // persisted WorkflowExecutionCommandEnvelope. Reflection-pin both halves of that invariant.
        var options = new WorkflowExecutionCommandDispatchOptions(new EmptyServiceProvider());
        var request = new StimulusDispatchRequest(
            stimulusType: "Event",
            stimulusHash: "sha256:event:hello",
            correlationId: "order-7",
            metadata: new Dictionary<string, string> { ["caller"] = "unit-test" },
            dispatchOptions: options);

        // The options ride only on the dedicated property, and it IS the non-serialized options type.
        Assert.Same(options, request.DispatchOptions);
        Assert.Equal(typeof(WorkflowExecutionCommandDispatchOptions), typeof(StimulusDispatchRequest).GetProperty(nameof(StimulusDispatchRequest.DispatchOptions))!.PropertyType);

        // No property on the request surfaces an IServiceProvider through a durable channel: only DispatchOptions
        // touches services, and it is excluded from the metadata that reaches the envelope.
        Assert.DoesNotContain(
            typeof(StimulusDispatchRequest).GetProperties(),
            property => typeof(IServiceProvider).IsAssignableFrom(property.PropertyType));

        // BuildDispatchMetadata is the only channel that flows into the durable envelope. It is a string→string map
        // (structurally incapable of carrying a service provider) and must not leak the options in any form.
        var dispatchMetadata = request.BuildDispatchMetadata();
        Assert.IsAssignableFrom<IReadOnlyDictionary<string, string>>(dispatchMetadata);
        Assert.DoesNotContain(dispatchMetadata.Values, value => value.Contains("ServiceProvider", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("unit-test", dispatchMetadata["caller"]);
    }

    [Fact]
    public void CommandKinds_IncludeActorStyleAgentVocabulary()
    {
        var names = Enum.GetNames<WorkflowExecutionCommandKind>();

        Assert.Contains(nameof(WorkflowExecutionCommandKind.ScheduleActivity), names);
        Assert.Contains(nameof(WorkflowExecutionCommandKind.StartActivity), names);
        Assert.Contains(nameof(WorkflowExecutionCommandKind.InvokeActivity), names);
        Assert.Contains(nameof(WorkflowExecutionCommandKind.CompleteActivity), names);
        Assert.Contains(nameof(WorkflowExecutionCommandKind.DeliverSignal), names);
        Assert.Contains(nameof(WorkflowExecutionCommandKind.CreateBookmark), names);
        Assert.Contains(nameof(WorkflowExecutionCommandKind.Checkpoint), names);
    }

    [Fact]
    public void AgentDescriptor_IsFrameworkNeutralAndKeepsCheckpointStateAuthoritative()
    {
        var descriptor = new WorkflowExecutionActorDescriptor(
            workflowExecutionId: "wfexec-1",
            agentId: "agent-1",
            providerName: "InProcessMailboxProvider",
            status: WorkflowExecutionActorStatus.Active,
            capabilities: WorkflowExecutionActorCapabilities.InProcessMailbox | WorkflowExecutionActorCapabilities.Passivation,
            activatedAt: _now,
            lastCheckpointId: "checkpoint-1");

        Assert.Equal("wfexec-1", descriptor.WorkflowExecutionId);
        Assert.Equal("checkpoint-1", descriptor.LastCheckpointId);
        Assert.True(descriptor.Capabilities.HasFlag(WorkflowExecutionActorCapabilities.InProcessMailbox));
        Assert.DoesNotContain(
            typeof(WorkflowExecutionActorDescriptor).GetProperties().Select(property => property.PropertyType.ToString()),
            IsActorFrameworkReference);
    }

    [Fact]
    public void ActivationRequest_IsKeyedByWorkflowExecutionIdAndReason()
    {
        var request = new WorkflowExecutionActorActivationRequest(
            workflowExecutionId: "wfexec-1",
            reason: WorkflowExecutionActorActivationReason.ResumeBookmark,
            requestedAt: _now,
            requestedBy: "dispatcher-1",
            requiredCapabilities: WorkflowExecutionActorCapabilities.InProcessMailbox | WorkflowExecutionActorCapabilities.LeaseFencing);

        Assert.Equal("wfexec-1", request.WorkflowExecutionId);
        Assert.Equal(WorkflowExecutionActorActivationReason.ResumeBookmark, request.Reason);
        Assert.True(request.RequiredCapabilities.HasFlag(WorkflowExecutionActorCapabilities.LeaseFencing));
    }

    [Fact]
    public void PassivationRequest_NamesSafeBoundary()
    {
        var request = new WorkflowExecutionActorPassivationRequest(
            workflowExecutionId: "wfexec-1",
            boundary: WorkflowExecutionActorPassivationBoundary.AfterCheckpointCommit,
            requestedAt: _now,
            reason: "Host drain");

        Assert.Equal("wfexec-1", request.WorkflowExecutionId);
        Assert.Equal(WorkflowExecutionActorPassivationBoundary.AfterCheckpointCommit, request.Boundary);
        Assert.DoesNotContain(
            Enum.GetNames<WorkflowExecutionActorPassivationBoundary>(),
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
            envelopeId: "envelope-duplicate-blank",
            workflowExecutionId: "wfexec-1",
            status: WorkflowExecutionCommandDispatchStatus.Duplicate,
            recordedAt: _now,
            reason: " "));
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
        var runtimeCoreAssembly = typeof(IWorkflowExecutionActorProvider).Assembly;
        var referencedAssemblies = runtimeCoreAssembly.GetReferencedAssemblies().Select(assembly => assembly.Name).ToArray();

        Assert.DoesNotContain(referencedAssemblies, IsActorFrameworkReference);
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

    private sealed class EmptyServiceProvider : IServiceProvider
    {
        public object? GetService(Type serviceType) => null;
    }

    private static bool IsActorFrameworkReference(string? name) =>
        name is not null
        && (name.Contains("Orleans", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Dapr", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Proto.Actor", StringComparison.OrdinalIgnoreCase));
}
