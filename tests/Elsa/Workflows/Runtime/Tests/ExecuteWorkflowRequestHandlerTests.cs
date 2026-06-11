using Elsa.Workflows.Runtime.Api.Handlers;
using Elsa.Workflows.Runtime.Api.Requests;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class ExecuteWorkflowRequestHandlerTests
{
    [Fact]
    public async Task RejectsUnknownArtifactId()
    {
        var handler = new ExecuteWorkflowRequestHandler(NewDispatcher(new InMemoryWorkflowExecutableStore()));

        var exception = await Assert.ThrowsAsync<WorkflowExecutableNotFoundException>(() => handler.Handle(new ExecuteWorkflow("missing-artifact"), CancellationToken.None));

        Assert.Contains("missing-artifact", exception.Message);
        Assert.Equal("missing-artifact", exception.ArtifactId);
    }

    [Fact]
    public async Task ReturnsAgentDispatchView()
    {
        var store = new InMemoryWorkflowExecutableStore();
        await store.SaveAsync(NewExecutable());
        var handler = new ExecuteWorkflowRequestHandler(NewDispatcher(store));

        var result = await handler.Handle(new ExecuteWorkflow("artifact-1"), CancellationToken.None);

        Assert.Equal("wfexec-fixed", result.WorkflowExecutionId);
        Assert.Equal("artifact-1", result.ArtifactId);
        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted.ToString(), result.CommandDispatchStatus);
        Assert.Equal("envelope-fixed", result.EnvelopeId);
        Assert.Equal("recording:wfexec-fixed", result.AgentId);
        Assert.Equal("Recording", result.AgentProviderName);
    }

    [Fact]
    public void ExecuteWorkflowHandler_DoesNotDependOnInlineExecutor()
    {
        var constructorParameters = typeof(ExecuteWorkflowRequestHandler)
            .GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        Assert.DoesNotContain(typeof(IWorkflowExecutor), constructorParameters);
    }

    private static WorkflowExecutionStartDispatcher NewDispatcher(InMemoryWorkflowExecutableStore store) =>
        new(
            store,
            new RecordingAgentProvider(),
            new FixedRuntimeExecutionIdGenerator(),
            new FixedTimeProvider(new DateTimeOffset(2026, 6, 11, 12, 0, 0, TimeSpan.Zero)));

    private static WorkflowExecutable NewExecutable() =>
        new(
            identity: new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test"),
            nodes: [],
            edges: [],
            startNodeIds: [],
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: DateTimeOffset.UtcNow,
            publishedAt: DateTimeOffset.UtcNow,
            compatibilityMetadata: new Dictionary<string, string>());

    private sealed class RecordingAgentProvider : IWorkflowExecutionAgentProvider
    {
        public WorkflowExecutionAgentCapabilities Capabilities => WorkflowExecutionAgentCapabilities.None;

        public ValueTask<IWorkflowExecutionAgent> GetAgentAsync(WorkflowExecutionAgentActivationRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IWorkflowExecutionAgent>(new RecordingAgent(request.WorkflowExecutionId));

        public ValueTask PassivateAsync(WorkflowExecutionAgentPassivationRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class RecordingAgent(string workflowExecutionId) : IWorkflowExecutionAgent
    {
        public WorkflowExecutionAgentDescriptor Descriptor { get; } = new(
            workflowExecutionId: workflowExecutionId,
            agentId: $"recording:{workflowExecutionId}",
            providerName: "Recording",
            status: WorkflowExecutionAgentStatus.Active,
            capabilities: WorkflowExecutionAgentCapabilities.None,
            activatedAt: DateTimeOffset.UtcNow);

        public ValueTask<WorkflowExecutionCommandDispatchResult> EnqueueAsync(WorkflowExecutionCommandEnvelope envelope, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new WorkflowExecutionCommandDispatchResult(
                envelopeId: envelope.EnvelopeId,
                workflowExecutionId: envelope.WorkflowExecutionId,
                status: WorkflowExecutionCommandDispatchStatus.Accepted,
                recordedAt: DateTimeOffset.UtcNow));
    }

    private sealed class FixedRuntimeExecutionIdGenerator : IRuntimeExecutionIdGenerator
    {
        public string NewWorkflowExecutionId() => "wfexec-fixed";

        public string NewWorkflowExecutionCommandId() => "command-fixed";

        public string NewWorkflowExecutionCommandEnvelopeId() => "envelope-fixed";
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
