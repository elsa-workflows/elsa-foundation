using System.Text.Json;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeSchedulerCommandDrainDispatchTests
{
    private readonly DateTimeOffset _now = new(2026, 6, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ProcessAsync_EnqueuesSchedulerWorkBeforeDrain()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var drainer = new RecordingSchedulerDrainer(queue, _now);
        var observer = new RecordingSchedulerDrainObserver();
        var processor = new WorkflowSchedulerCommandProcessor(
            queue,
            drainer,
            new ImmediateWorkflowSchedulerDrainPolicy(),
            [observer],
            new FixedTimeProvider(_now));

        var envelope = NewEnvelope(1);

        await processor.ProcessAsync(envelope);

        var request = Assert.Single(drainer.Requests);
        var queuedItem = Assert.Single(drainer.QueueSnapshots.Single());
        var observed = Assert.Single(observer.ObservedResults);
        Assert.Equal("wfexec-1", request.WorkflowExecutionId);
        Assert.Equal(envelope.EnvelopeId, queuedItem.WorkItemId);
        Assert.Equal(_now, queuedItem.RecordedAt);
        Assert.Equal(envelope.EnvelopeId, observed.Items.Single().WorkItemId);
    }

    [Fact]
    public async Task ProcessAsync_CarriesDispatchAmbientServicesIntoDrainRequest()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var drainer = new RecordingSchedulerDrainer(queue, _now);
        var processor = new WorkflowSchedulerCommandProcessor(
            queue,
            drainer,
            new ImmediateWorkflowSchedulerDrainPolicy(),
            [],
            new FixedTimeProvider(_now));
        await using var ambientServices = new ServiceCollection().BuildServiceProvider();

        await processor.ProcessAsync(NewEnvelope(1), new WorkflowExecutionCommandDispatchOptions(ambientServices));

        Assert.Same(ambientServices, Assert.Single(drainer.Requests).AmbientServices);
    }

    [Fact]
    public async Task ProcessAsync_CanRecordSchedulerWorkWithoutDrainingWhenPolicyDefers()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var drainer = new RecordingSchedulerDrainer(queue, _now);
        var processor = new WorkflowSchedulerCommandProcessor(
            queue,
            drainer,
            DeferredSchedulerDrainPolicy.Instance,
            [],
            new FixedTimeProvider(_now));

        await processor.ProcessAsync(NewEnvelope(1));

        Assert.Empty(drainer.Requests);
        Assert.Single(await queue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
    }

    [Fact]
    public async Task ProcessAsync_RejectsDrainRequestForDifferentWorkflowExecution()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var drainer = new RecordingSchedulerDrainer(queue, _now);
        var processor = new WorkflowSchedulerCommandProcessor(
            queue,
            drainer,
            MismatchedSchedulerDrainPolicy.Instance,
            [],
            new FixedTimeProvider(_now));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => processor.ProcessAsync(NewEnvelope(1)).AsTask());

        Assert.Contains("wfexec-other", exception.Message);
        Assert.Contains("wfexec-1", exception.Message);
        Assert.Empty(drainer.Requests);
    }

    [Fact]
    public async Task ProcessAsync_ReportsFaultedDrainResultToObserversWithoutThrowing()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var observer = new RecordingSchedulerDrainObserver();
        var drainer = new FaultingResultSchedulerDrainer(_now);
        var processor = new WorkflowSchedulerCommandProcessor(
            queue,
            drainer,
            new ImmediateWorkflowSchedulerDrainPolicy(),
            [observer],
            new FixedTimeProvider(_now));

        await processor.ProcessAsync(NewEnvelope(1));

        var result = Assert.Single(observer.ObservedResults);
        Assert.True(result.StoppedOnFault);
        Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Faulted, result.Items.Single().Status);
    }

    [Fact]
    public async Task ProcessAsync_AttemptsAllObserversBeforeReportingObserverFailures()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var recordingObserver = new RecordingSchedulerDrainObserver();
        var processor = new WorkflowSchedulerCommandProcessor(
            queue,
            new RecordingSchedulerDrainer(queue, _now),
            new ImmediateWorkflowSchedulerDrainPolicy(),
            [new ThrowingSchedulerDrainObserver(), recordingObserver],
            new FixedTimeProvider(_now));

        var exception = await Assert.ThrowsAsync<AggregateException>(() => processor.ProcessAsync(NewEnvelope(1)).AsTask());

        Assert.Contains("scheduler drain observers failed", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Single(recordingObserver.ObservedResults);
    }

    [Fact]
    public async Task ProcessAsync_PreservesPriorObserverFailuresWhenLaterObserverCancels()
    {
        using var cancellation = new CancellationTokenSource();
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var processor = new WorkflowSchedulerCommandProcessor(
            queue,
            new RecordingSchedulerDrainer(queue, _now),
            new ImmediateWorkflowSchedulerDrainPolicy(),
            [new ThrowingSchedulerDrainObserver(), new CancelingSchedulerDrainObserver(cancellation)],
            new FixedTimeProvider(_now));

        var exception = await Assert.ThrowsAsync<AggregateException>(() => processor.ProcessAsync(NewEnvelope(1), cancellation.Token).AsTask());

        Assert.Equal(2, exception.InnerExceptions.Count);
        Assert.Contains(exception.InnerExceptions, inner => inner is InvalidOperationException);
        Assert.Contains(exception.InnerExceptions, inner => inner is OperationCanceledException);
    }

    [Fact]
    public async Task NoopObserver_DoesNotThrowWhenCancellationIsAlreadyRequested()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        var observer = new NoopWorkflowSchedulerDrainObserver();

        await observer.OnDrainedAsync(NewEnvelope(1), EmptyDrainResult("wfexec-1"), cancellation.Token);
    }

    [Fact]
    public async Task InProcessAgent_DrainsAcceptedCommandsThroughRuntimeComposition()
    {
        var services = new ServiceCollection();
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);
        await using var provider = services.BuildServiceProvider();
        var agentProvider = provider.GetRequiredService<IWorkflowExecutionAgentProvider>();
        var queue = provider.GetRequiredService<IWorkflowSchedulerWorkQueue>();
        var agent = await agentProvider.GetAgentAsync(NewActivationRequest("wfexec-1"));

        var result = await agent.EnqueueAsync(NewEnvelope(1));
        var queuedItems = await queue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1"));

        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted, result.Status);
        Assert.Empty(queuedItems);
    }

    [Fact]
    public async Task InProcessAgent_StartCommandFaultsAtInvokeActivityWhenNoActivityInvocationProviderIsComposed()
    {
        var observer = new RecordingSchedulerDrainObserver();
        var services = new ServiceCollection();
        services.AddSingleton<IWorkflowSchedulerDrainObserver>(observer);
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);
        await using var provider = services.BuildServiceProvider();
        var store = provider.GetRequiredService<IWorkflowExecutableStore>();
        var activityStateStore = provider.GetRequiredService<IActivityExecutionStateStore>();
        var executable = NewExecutable();
        await store.SaveAsync(executable);
        var agentProvider = provider.GetRequiredService<IWorkflowExecutionAgentProvider>();
        var queue = provider.GetRequiredService<IWorkflowSchedulerWorkQueue>();
        var agent = await agentProvider.GetAgentAsync(NewActivationRequest("wfexec-1"));

        var result = await agent.EnqueueAsync(NewStartEnvelope(executable.Identity));
        var queuedItems = await queue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1"));
        var activityStates = await activityStateStore.ListAsync("wfexec-1");
        var drainResult = Assert.Single(observer.ObservedResults);

        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted, result.Status);
        Assert.Empty(queuedItems);
        var state = Assert.Single(activityStates);
        Assert.Equal("node-start", state.Execution.ExecutableNodeId);
        Assert.Equal(ActivityExecutionStatus.Running, state.Status);
        Assert.True(drainResult.StoppedOnFault);
        Assert.Equal(
            new[] { WorkflowExecutionCommandKind.Start, WorkflowExecutionCommandKind.Checkpoint, WorkflowExecutionCommandKind.ScheduleActivity, WorkflowExecutionCommandKind.StartActivity, WorkflowExecutionCommandKind.InvokeActivity },
            drainResult.Items.Select(item => item.CommandKind).ToArray());
        var invokeResult = drainResult.Items.Last();
        Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Faulted, invokeResult.Status);
        Assert.Equal(MissingActivityInvocationSchedulerWorkHandler.HandlerName, invokeResult.HandlerName);
        Assert.Contains("no activity invocation provider", invokeResult.Error, StringComparison.OrdinalIgnoreCase);
    }

    private WorkflowExecutionAgentActivationRequest NewActivationRequest(string workflowExecutionId) =>
        new(
            workflowExecutionId: workflowExecutionId,
            reason: WorkflowExecutionAgentActivationReason.Start,
            requestedAt: _now,
            requestedBy: "runtime-test",
            requiredCapabilities: WorkflowExecutionAgentCapabilities.InProcessMailbox);

    private WorkflowExecutionCommandEnvelope NewEnvelope(int index, string workflowExecutionId = "wfexec-1")
    {
        using var document = JsonDocument.Parse($$"""{"workItemId":"work-{{index}}"}""");
        var command = new WorkflowExecutionCommand(
            CommandId: $"command-{index}",
            WorkflowExecutionId: workflowExecutionId,
            Kind: WorkflowExecutionCommandKind.RunSchedulerWork,
            EnqueuedAt: _now,
            Payload: document.RootElement.Clone(),
            Metadata: new Dictionary<string, string> { ["source"] = "test" });

        return new(
            envelopeId: $"envelope-{index}",
            workflowExecutionId: workflowExecutionId,
            command: command,
            idempotencyKey: $"{workflowExecutionId}:command-{index}",
            deliveryMode: WorkflowExecutionCommandDeliveryMode.AtLeastOnce,
            enqueuedAt: _now,
            sequence: index,
            metadata: new Dictionary<string, string> { ["transport"] = "in-process" });
    }

    private WorkflowExecutionCommandEnvelope NewStartEnvelope(WorkflowExecutableIdentity pinnedExecutable)
    {
        var payload = new WorkflowExecutionStartCommandPayload(pinnedExecutable, pinnedExecutable.ArtifactId);
        var command = new WorkflowExecutionCommand(
            CommandId: "command-start",
            WorkflowExecutionId: "wfexec-1",
            Kind: WorkflowExecutionCommandKind.Start,
            EnqueuedAt: _now,
            Payload: JsonSerializer.SerializeToElement(payload),
            Metadata: new Dictionary<string, string> { ["source"] = "test" });

        return new(
            envelopeId: "envelope-start",
            workflowExecutionId: "wfexec-1",
            command: command,
            idempotencyKey: "wfexec-1:start:artifact-1",
            deliveryMode: WorkflowExecutionCommandDeliveryMode.AtLeastOnce,
            enqueuedAt: _now,
            sequence: 1,
            metadata: new Dictionary<string, string> { ["transport"] = "in-process" });
    }

    private static WorkflowExecutable NewExecutable()
    {
        using var document = JsonDocument.Parse("""{"type":"test"}""");
        var node = new ExecutableNode(
            executableNodeId: "node-start",
            authoredActivityId: "authored-node-start",
            activityType: "test/activity",
            activityTypeVersion: "1.0.0",
            descriptorType: "test",
            descriptorPayload: document.RootElement.Clone(),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>());

        return new(
            identity: new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test"),
            nodes: [node],
            edges: [],
            startNodeIds: ["node-start"],
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: DateTimeOffset.UtcNow,
            publishedAt: DateTimeOffset.UtcNow,
            compatibilityMetadata: new Dictionary<string, string>());
    }

    private RuntimeSchedulerDrainResult EmptyDrainResult(string workflowExecutionId) =>
        new(
            workflowExecutionId: workflowExecutionId,
            startedAt: _now,
            completedAt: _now,
            items: []);

    private sealed class RecordingSchedulerDrainer(
        IWorkflowSchedulerWorkQueue queue,
        DateTimeOffset now) : IWorkflowSchedulerDrainer
    {
        public List<RuntimeSchedulerDrainRequest> Requests { get; } = [];
        public List<IReadOnlyCollection<RuntimeSchedulerWorkItem>> QueueSnapshots { get; } = [];

        public async ValueTask<RuntimeSchedulerDrainResult> DrainAsync(RuntimeSchedulerDrainRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            var items = await queue.ListAsync(new RuntimeSchedulerWorkQuery(request.WorkflowExecutionId), cancellationToken);
            QueueSnapshots.Add(items);
            return new RuntimeSchedulerDrainResult(
                workflowExecutionId: request.WorkflowExecutionId,
                startedAt: now,
                completedAt: now,
                items: items.Select(item => new RuntimeSchedulerWorkItemResult(
                    workItemId: item.WorkItemId,
                    workflowExecutionId: item.WorkflowExecutionId,
                    commandKind: item.CommandKind,
                    status: RuntimeSchedulerWorkItemResultStatus.Completed,
                    handlerName: nameof(RecordingSchedulerDrainer),
                    startedAt: now,
                    completedAt: now)).ToArray());
        }
    }

    private sealed class FaultingResultSchedulerDrainer(DateTimeOffset now) : IWorkflowSchedulerDrainer
    {
        public ValueTask<RuntimeSchedulerDrainResult> DrainAsync(RuntimeSchedulerDrainRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new RuntimeSchedulerDrainResult(
                workflowExecutionId: request.WorkflowExecutionId,
                startedAt: now,
                completedAt: now,
                items:
                [
                    new RuntimeSchedulerWorkItemResult(
                        workItemId: "work-1",
                        workflowExecutionId: request.WorkflowExecutionId,
                        commandKind: WorkflowExecutionCommandKind.RunSchedulerWork,
                        status: RuntimeSchedulerWorkItemResultStatus.Faulted,
                        handlerName: nameof(FaultingResultSchedulerDrainer),
                        startedAt: now,
                        completedAt: now,
                        error: "Faulted for test.")
                ]));
    }

    private sealed class RecordingSchedulerDrainObserver : IWorkflowSchedulerDrainObserver
    {
        public List<RuntimeSchedulerDrainResult> ObservedResults { get; } = [];

        public ValueTask OnDrainedAsync(
            WorkflowExecutionCommandEnvelope envelope,
            RuntimeSchedulerDrainResult result,
            CancellationToken cancellationToken = default)
        {
            ObservedResults.Add(result);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class ThrowingSchedulerDrainObserver : IWorkflowSchedulerDrainObserver
    {
        public ValueTask OnDrainedAsync(
            WorkflowExecutionCommandEnvelope envelope,
            RuntimeSchedulerDrainResult result,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Observer failed for test.");
    }

    private sealed class CancelingSchedulerDrainObserver(CancellationTokenSource cancellation) : IWorkflowSchedulerDrainObserver
    {
        public ValueTask OnDrainedAsync(
            WorkflowExecutionCommandEnvelope envelope,
            RuntimeSchedulerDrainResult result,
            CancellationToken cancellationToken = default)
        {
            cancellation.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class DeferredSchedulerDrainPolicy : IWorkflowSchedulerDrainPolicy
    {
        public static readonly DeferredSchedulerDrainPolicy Instance = new();

        private DeferredSchedulerDrainPolicy()
        {
        }

        public RuntimeSchedulerDrainRequest? CreateDrainRequest(
            WorkflowExecutionCommandEnvelope envelope,
            RuntimeSchedulerWorkItem workItem) => null;
    }

    private sealed class MismatchedSchedulerDrainPolicy : IWorkflowSchedulerDrainPolicy
    {
        public static readonly MismatchedSchedulerDrainPolicy Instance = new();

        private MismatchedSchedulerDrainPolicy()
        {
        }

        public RuntimeSchedulerDrainRequest? CreateDrainRequest(
            WorkflowExecutionCommandEnvelope envelope,
            RuntimeSchedulerWorkItem workItem) =>
            new("wfexec-other");
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
