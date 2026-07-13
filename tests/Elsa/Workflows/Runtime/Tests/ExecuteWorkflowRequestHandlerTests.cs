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
        var store = new InMemoryWorkflowExecutableStore();
        var handler = new ExecuteWorkflowRequestHandler(NewDispatcher(store), store);

        var exception = await Assert.ThrowsAsync<WorkflowExecutableNotFoundException>(() => handler.Handle(new ExecuteWorkflow("missing-artifact"), CancellationToken.None));

        Assert.Contains("missing-artifact", exception.Message);
        Assert.Equal("missing-artifact", exception.ArtifactId);
    }

    [Fact]
    public async Task ReturnsAgentDispatchView()
    {
        var store = new InMemoryWorkflowExecutableStore();
        await store.SaveAsync(NewExecutable());
        var handler = new ExecuteWorkflowRequestHandler(NewDispatcher(store), store);

        var result = await handler.Handle(new ExecuteWorkflow("artifact-1"), CancellationToken.None);

        Assert.Equal("wfexec-fixed", result.WorkflowExecutionId);
        Assert.Equal("artifact-1", result.ArtifactId);
        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted.ToString(), result.CommandDispatchStatus);
        Assert.Equal("envelope-fixed", result.EnvelopeId);
        Assert.Equal("recording:wfexec-fixed", result.AgentId);
        Assert.Equal("Recording", result.AgentProviderName);
    }

    [Fact]
    public async Task ClassifiesDirectExecutableStartsAsPublishedRuns()
    {
        var store = new InMemoryWorkflowExecutableStore();
        await store.SaveAsync(NewExecutable());
        var dispatcher = new CapturingStartDispatcher(NewDispatcher(store));
        var handler = new ExecuteWorkflowRequestHandler(dispatcher, store);

        await handler.Handle(new ExecuteWorkflow("artifact-1"), CancellationToken.None);

        Assert.Equal(WorkflowRunKind.PublishedRun, Assert.Single(dispatcher.Requests).RunKind);
    }

    [Fact]
    public void ExecuteWorkflowHandler_DoesNotDependOnInlineExecutor()
    {
        var runtimeCoreAssembly = typeof(IWorkflowStartDispatcher).Assembly;
        var constructorParameters = typeof(ExecuteWorkflowRequestHandler)
            .GetConstructors()
            .SelectMany(constructor => constructor.GetParameters())
            .Select(parameter => parameter.ParameterType.FullName)
            .ToArray();

        Assert.DoesNotContain("Elsa.Workflows.Runtime.Core.Contracts.IWorkflowExecutor", constructorParameters);
        Assert.Null(runtimeCoreAssembly.GetType("Elsa.Workflows.Runtime.Core.Contracts.IWorkflowExecutor"));
    }

    private static WorkflowStartDispatcher NewDispatcher(InMemoryWorkflowExecutableStore store) =>
        new(
            store,
            new InMemoryWorkflowExecutableSourceReferenceStore(),
            new RecordingAgentProvider(),
            new FixedRuntimeExecutionIdGenerator(),
            new FixedTimeProvider(new DateTimeOffset(2026, 6, 11, 12, 0, 0, TimeSpan.Zero)));

    private static WorkflowExecutable NewExecutable() =>
        new(
            identity: new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test"),
            rootActivity: NewNode("node-root"),
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: DateTimeOffset.UtcNow,
            compatibilityMetadata: new Dictionary<string, string>());

    private static ExecutableNode NewNode(string nodeId) =>
        new(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: "test/activity",
            activityTypeVersion: "1.0.0",
            descriptorType: "test",
            descriptorPayload: System.Text.Json.JsonSerializer.SerializeToElement(new { type = "test" }),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>());

    private sealed class RecordingAgentProvider : IWorkflowExecutionActorProvider
    {
        public WorkflowExecutionActorCapabilities Capabilities => WorkflowExecutionActorCapabilities.None;

        public ValueTask<IWorkflowExecutionActor> GetAgentAsync(WorkflowExecutionActorActivationRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IWorkflowExecutionActor>(new RecordingAgent(request.WorkflowExecutionId));

        public ValueTask PassivateAsync(WorkflowExecutionActorPassivationRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;
    }

    private sealed class RecordingAgent(string workflowExecutionId) : IWorkflowExecutionActor
    {
        public WorkflowExecutionActorDescriptor Descriptor { get; } = new(
            workflowExecutionId: workflowExecutionId,
            agentId: $"recording:{workflowExecutionId}",
            providerName: "Recording",
            status: WorkflowExecutionActorStatus.Active,
            capabilities: WorkflowExecutionActorCapabilities.None,
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

        public string NewActivityExecutionId() => throw new NotSupportedException("HTTP start dispatch does not schedule activities.");
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class CapturingStartDispatcher(IWorkflowStartDispatcher inner) : IWorkflowStartDispatcher
    {
        public List<WorkflowExecutionStartDispatchRequest> Requests { get; } = [];

        public ValueTask<WorkflowExecutionStartDispatchResult> DispatchAsync(
            WorkflowExecutionStartDispatchRequest request,
            WorkflowExecutableReferenceScope requiredScope = WorkflowExecutableReferenceScope.Published,
            WorkflowExecutionCommandDispatchOptions? dispatchOptions = null,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            return inner.DispatchAsync(request, requiredScope, dispatchOptions, cancellationToken);
        }
    }
}
