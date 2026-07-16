using System.Text.Json;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
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
        var processor = NewProcessor(
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
        var processor = NewProcessor(
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
    public async Task ProcessAsync_DeliversSchedulerPostCommitWorkAndContinuesDraining()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var drainer = new DequeuingSchedulerDrainer(queue, _now);
        var outboxProcessor = new EnqueueOncePostCommitOutboxProcessor(queue, FollowUpWorkItem());
        var observer = new RecordingSchedulerDrainObserver();
        var processor = NewProcessor(
            queue,
            drainer,
            new ImmediateWorkflowSchedulerDrainPolicy(),
            [observer],
            new FixedTimeProvider(_now),
            outboxProcessor);

        await processor.ProcessAsync(NewEnvelope(1));

        var observed = Assert.Single(observer.ObservedResults);
        Assert.Equal(2, drainer.Requests.Count);
        Assert.Equal(2, outboxProcessor.Requests.Count);
        Assert.All(outboxProcessor.Requests, request => Assert.Equal(RuntimePostCommitIntentKinds.EnqueueSchedulerWork, request.IntentKind));
        Assert.Equal(RuntimeSchedulerDrainStopReason.Quiesced, observed.StopReason);
        Assert.Equal(2, observed.OutboxDeliveryResults.Count);
        Assert.Equal(1, observed.OutboxAttemptedCount);
        Assert.Equal(1, observed.OutboxDeliveredCount);
        Assert.Equal(0, observed.OutboxFailedCount);
        Assert.Equal(
            new[] { WorkflowExecutionCommandKind.RunSchedulerWork, WorkflowExecutionCommandKind.ScheduleActivity },
            observed.Items.Select(item => item.CommandKind).ToArray());
        Assert.Empty(await queue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
    }

    [Fact]
    public async Task ProcessAsync_CanRecordSchedulerWorkWithoutDrainingWhenPolicyDefers()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var drainer = new RecordingSchedulerDrainer(queue, _now);
        var processor = NewProcessor(
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
        var processor = NewProcessor(
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
        var processor = NewProcessor(
            queue,
            drainer,
            new ImmediateWorkflowSchedulerDrainPolicy(),
            [observer],
            new FixedTimeProvider(_now));

        await processor.ProcessAsync(NewEnvelope(1));

        var result = Assert.Single(observer.ObservedResults);
        Assert.Equal(RuntimeSchedulerDrainStopReason.Faulted, result.StopReason);
        Assert.True(result.StoppedOnFault);
        Assert.Equal(RuntimeSchedulerWorkItemResultStatus.Faulted, result.Items.Single().Status);
    }

    [Fact]
    public async Task ProcessAsync_ReturnsFaultedProcessResultWhenDrainFaults()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var processor = NewProcessor(
            queue,
            new FaultingResultSchedulerDrainer(_now),
            new ImmediateWorkflowSchedulerDrainPolicy(),
            [],
            new FixedTimeProvider(_now));

        // RT-14: the drain result must not be discarded — the processor surfaces the fault verdict to its caller.
        var result = await processor.ProcessAsync(NewEnvelope(1));

        Assert.True(result.IsFaulted);
        Assert.True(result.Faulted);
        Assert.Equal(RuntimeSchedulerDrainStopReason.Faulted, result.StopReason);
        Assert.Contains("Faulted for test.", result.FaultReason);
    }

    [Fact]
    public async Task ProcessAsync_ReturnsNoDrainWhenPolicyDefers()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var processor = NewProcessor(
            queue,
            new RecordingSchedulerDrainer(queue, _now),
            DeferredSchedulerDrainPolicy.Instance,
            [],
            new FixedTimeProvider(_now));

        var result = await processor.ProcessAsync(NewEnvelope(1));

        Assert.False(result.DrainPerformed);
        Assert.False(result.IsFaulted);
    }

    [Fact]
    public async Task ProcessAsync_ReportsOutboxDeliveryFailureStopReasonToObservers()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var drainer = new DequeuingSchedulerDrainer(queue, _now);
        var outboxProcessor = new FailingPostCommitOutboxProcessor();
        var observer = new RecordingSchedulerDrainObserver();
        var processor = NewProcessor(
            queue,
            drainer,
            new ImmediateWorkflowSchedulerDrainPolicy(),
            [observer],
            new FixedTimeProvider(_now),
            outboxProcessor);

        await processor.ProcessAsync(NewEnvelope(1));

        var observed = Assert.Single(observer.ObservedResults);
        Assert.Equal(RuntimeSchedulerDrainStopReason.OutboxDeliveryFailed, observed.StopReason);
        Assert.Equal(1, observed.OutboxAttemptedCount);
        Assert.Equal(0, observed.OutboxDeliveredCount);
        Assert.Equal(1, observed.OutboxFailedCount);
        Assert.Single(drainer.Requests);
    }

    [Fact]
    public async Task ProcessAsync_PreservesOutboxDeliveryFailureStopReasonAfterMixedDeliveryBatch()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var drainer = new DequeuingSchedulerDrainer(queue, _now);
        var outboxProcessor = new MixedDeliveryPostCommitOutboxProcessor(queue, FollowUpWorkItem());
        var observer = new RecordingSchedulerDrainObserver();
        var processor = NewProcessor(
            queue,
            drainer,
            new ImmediateWorkflowSchedulerDrainPolicy(),
            [observer],
            new FixedTimeProvider(_now),
            outboxProcessor);

        await processor.ProcessAsync(NewEnvelope(1));

        var observed = Assert.Single(observer.ObservedResults);
        Assert.Equal(2, drainer.Requests.Count);
        Assert.Equal(2, outboxProcessor.Requests.Count);
        Assert.Equal(RuntimeSchedulerDrainStopReason.OutboxDeliveryFailed, observed.StopReason);
        Assert.Equal(2, observed.OutboxAttemptedCount);
        Assert.Equal(1, observed.OutboxDeliveredCount);
        Assert.Equal(1, observed.OutboxFailedCount);
        Assert.Equal(
            new[] { WorkflowExecutionCommandKind.RunSchedulerWork, WorkflowExecutionCommandKind.ScheduleActivity },
            observed.Items.Select(item => item.CommandKind).ToArray());
        Assert.Empty(await queue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
    }

    [Fact]
    public async Task ProcessAsync_StopsOnPauseAfterPostCommitDeliveryAndLeavesDeliveredWorkQueued()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var followUpWorkItem = FollowUpWorkItem();
        var drainer = new PauseAfterFirstDrainSchedulerDrainer(queue, _now, followUpWorkItem.WorkItemId);
        var outboxProcessor = new EnqueueOncePostCommitOutboxProcessor(queue, followUpWorkItem);
        var observer = new RecordingSchedulerDrainObserver();
        var processor = NewProcessor(
            queue,
            drainer,
            new ImmediateWorkflowSchedulerDrainPolicy(),
            [observer],
            new FixedTimeProvider(_now),
            outboxProcessor);

        await processor.ProcessAsync(NewEnvelope(1));

        var observed = Assert.Single(observer.ObservedResults);
        Assert.Equal(RuntimeSchedulerDrainStopReason.Paused, observed.StopReason);
        Assert.True(observed.StoppedOnPause);
        var queuedItem = Assert.Single(await queue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1")));
        Assert.Equal(followUpWorkItem.WorkItemId, queuedItem.WorkItemId);
    }

    [Fact]
    public async Task ProcessAsync_PropagatesOutboxCancellation()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var processor = NewProcessor(
            queue,
            new DequeuingSchedulerDrainer(queue, _now),
            new ImmediateWorkflowSchedulerDrainPolicy(),
            [],
            new FixedTimeProvider(_now),
            CancelingPostCommitOutboxProcessor.Instance);

        await Assert.ThrowsAsync<OperationCanceledException>(() => processor.ProcessAsync(NewEnvelope(1)).AsTask());
    }

    [Fact]
    public async Task ProcessAsync_ThrowsDomainExceptionWhenCycleCapIsExceeded()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var options = new WorkflowDrainOrchestratorOptions(maxDrainCycles: 2, outboxDeliveryBatchSize: 1);
        var processor = NewProcessor(
            queue,
            new DequeuingSchedulerDrainer(queue, _now),
            new ImmediateWorkflowSchedulerDrainPolicy(),
            [],
            new FixedTimeProvider(_now),
            AlwaysDeliveringPostCommitOutboxProcessor.Instance,
            options);

        var exception = await Assert.ThrowsAsync<DrainCycleLimitExceededException>(() => processor.ProcessAsync(NewEnvelope(1)).AsTask());

        Assert.Equal("wfexec-1", exception.WorkflowExecutionId);
        Assert.Equal(2, exception.MaxDrainCycles);
    }

    [Fact]
    public async Task ProcessAsync_AttemptsAllObserversBeforeReportingObserverFailures()
    {
        var queue = new InMemoryWorkflowSchedulerWorkQueue();
        var recordingObserver = new RecordingSchedulerDrainObserver();
        var processor = NewProcessor(
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
        var processor = NewProcessor(
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
        var agentProvider = provider.GetRequiredService<IWorkflowExecutionActorProvider>();
        var queue = provider.GetRequiredService<IWorkflowSchedulerWorkQueue>();
        var agent = await agentProvider.GetAgentAsync(NewActivationRequest("wfexec-1"));

        var result = await agent.EnqueueAsync(NewEnvelope(1));
        var queuedItems = await queue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1"));

        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted, result.Status);
        Assert.Empty(queuedItems);
    }

    [Fact]
    public async Task InProcessAgent_ReturnsAcceptedButFaultedWhenDrainFaults()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IWorkflowSchedulerWorkHandler, AlwaysFaultingSchedulerWorkHandler>();
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);
        await using var provider = services.BuildServiceProvider();
        var agentProvider = provider.GetRequiredService<IWorkflowExecutionActorProvider>();
        var poisonStore = provider.GetRequiredService<IWorkflowSchedulerPoisonStore>();
        var agent = await agentProvider.GetAgentAsync(NewActivationRequest("wfexec-1"));

        var result = await agent.EnqueueAsync(NewEnvelope(1));

        // RT-14 acceptance: a faulting turn yields a non-success dispatch outcome (not silent Accepted)...
        Assert.Equal(WorkflowExecutionCommandDispatchStatus.AcceptedButFaulted, result.Status);
        Assert.False(string.IsNullOrWhiteSpace(result.Reason));
        // ...and RT-1 gap b: the crashed handler's work item is recorded to the poison store, not dropped.
        var poison = Assert.Single(await poisonStore.ListAsync("wfexec-1"));
        Assert.Equal(RuntimeSchedulerPoisonDisposition.Poisoned, poison.Disposition); // Default Noop retry policy.
    }

    [Fact]
    public async Task InProcessAgent_StartCommandProcessesPostCommitSchedulerWorkAndRecordsActivityState()
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
        var agentProvider = provider.GetRequiredService<IWorkflowExecutionActorProvider>();
        var queue = provider.GetRequiredService<IWorkflowSchedulerWorkQueue>();
        var outboxStore = provider.GetRequiredService<IRuntimePostCommitOutboxStore>();
        var agent = await agentProvider.GetAgentAsync(NewActivationRequest("wfexec-1"));

        var result = await agent.EnqueueAsync(NewStartEnvelope(executable.Identity));
        var queuedItems = await queue.ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1"));
        var activityStates = await activityStateStore.ListAsync("wfexec-1");
        var pending = await outboxStore.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(_now.AddYears(1), limit: 10, workflowExecutionId: "wfexec-1"));
        var drainResult = Assert.Single(observer.ObservedResults);
        var rootState = Assert.Single(activityStates);

        // The missing-activity handler faults the turn, so RT-14 surfaces AcceptedButFaulted instead of
        // masking the fault as a plain Accepted (the drain still records activity state + delivers the outbox).
        Assert.Equal(WorkflowExecutionCommandDispatchStatus.AcceptedButFaulted, result.Status);
        Assert.Empty(queuedItems);
        Assert.Empty(pending);
        Assert.Equal("node-start", rootState.Execution.ExecutableNodeId);
        Assert.Equal(ActivityExecutionStatus.Running, rootState.Status);
        Assert.True(drainResult.StoppedOnFault);
        Assert.True(drainResult.OutboxDeliveredCount > 0);
        Assert.Equal(
            new[]
            {
                WorkflowExecutionCommandKind.Start,
                WorkflowExecutionCommandKind.Checkpoint,
                WorkflowExecutionCommandKind.ScheduleActivity,
                WorkflowExecutionCommandKind.StartActivity,
                WorkflowExecutionCommandKind.InvokeActivity
            },
            drainResult.Items.Select(item => item.CommandKind).ToArray());
        Assert.Contains(drainResult.Items, item => item.HandlerName == MissingActivityInvocationSchedulerWorkHandler.HandlerName);
    }

    private WorkflowExecutionActorActivationRequest NewActivationRequest(string workflowExecutionId) =>
        new(
            workflowExecutionId: workflowExecutionId,
            reason: WorkflowExecutionActorActivationReason.Start,
            requestedAt: _now,
            requestedBy: "runtime-test",
            requiredCapabilities: WorkflowExecutionActorCapabilities.InProcessMailbox);

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
            descriptor: new RuntimeActivityDescriptor("test", RuntimeActivityDescriptor.InitialSchemaVersion, document.RootElement.Clone()),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>());

        return new(
            identity: new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test"),
            rootActivity: node,
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: DateTimeOffset.UtcNow,
            compatibilityMetadata: new Dictionary<string, string>());
    }

    private RuntimeSchedulerDrainResult EmptyDrainResult(string workflowExecutionId) =>
        new(
            workflowExecutionId: workflowExecutionId,
            startedAt: _now,
            completedAt: _now,
            items: []);

    private static WorkflowSchedulerCommandRouter NewProcessor(
        IWorkflowSchedulerWorkQueue queue,
        IWorkflowSchedulerDrainer drainer,
        IWorkflowSchedulerDrainPolicy drainPolicy,
        IEnumerable<IWorkflowSchedulerDrainObserver> observers,
        TimeProvider timeProvider,
        IRuntimePostCommitOutboxProcessor? outboxProcessor = null,
        WorkflowDrainOrchestratorOptions? options = null) =>
        new(
            queue,
            drainPolicy,
            new WorkflowDrainOrchestrator(drainer, outboxProcessor ?? EmptyPostCommitOutboxProcessor.Instance, observers, options),
            timeProvider);

    private RuntimeSchedulerWorkItem FollowUpWorkItem() =>
        new(
            workItemId: "work-follow-up",
            workflowExecutionId: "wfexec-1",
            commandId: "command-follow-up",
            commandKind: WorkflowExecutionCommandKind.ScheduleActivity,
            envelopeId: "envelope-follow-up",
            idempotencyKey: "wfexec-1:command-follow-up",
            enqueuedAt: _now,
            recordedAt: _now,
            sequence: 2);

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

    private sealed class DequeuingSchedulerDrainer(
        InMemoryWorkflowSchedulerWorkQueue queue,
        DateTimeOffset now) : IWorkflowSchedulerDrainer
    {
        public List<RuntimeSchedulerDrainRequest> Requests { get; } = [];

        public async ValueTask<RuntimeSchedulerDrainResult> DrainAsync(RuntimeSchedulerDrainRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            var results = new List<RuntimeSchedulerWorkItemResult>();

            while (await queue.DequeueAsync(request.WorkflowExecutionId, cancellationToken) is { } item)
            {
                results.Add(new RuntimeSchedulerWorkItemResult(
                    workItemId: item.WorkItemId,
                    workflowExecutionId: item.WorkflowExecutionId,
                    commandKind: item.CommandKind,
                    status: RuntimeSchedulerWorkItemResultStatus.Completed,
                    handlerName: nameof(DequeuingSchedulerDrainer),
                    startedAt: now,
                    completedAt: now));
            }

            return new RuntimeSchedulerDrainResult(
                workflowExecutionId: request.WorkflowExecutionId,
                startedAt: now,
                completedAt: now,
                items: results);
        }
    }

    private sealed class PauseAfterFirstDrainSchedulerDrainer(
        InMemoryWorkflowSchedulerWorkQueue queue,
        DateTimeOffset now,
        string pausedWorkItemId) : IWorkflowSchedulerDrainer
    {
        private bool _pauseNextDrain;

        public async ValueTask<RuntimeSchedulerDrainResult> DrainAsync(RuntimeSchedulerDrainRequest request, CancellationToken cancellationToken = default)
        {
            if (_pauseNextDrain)
            {
                return new RuntimeSchedulerDrainResult(
                    workflowExecutionId: request.WorkflowExecutionId,
                    startedAt: now,
                    completedAt: now,
                    items:
                    [
                        new RuntimeSchedulerWorkItemResult(
                            workItemId: pausedWorkItemId,
                            workflowExecutionId: request.WorkflowExecutionId,
                            commandKind: WorkflowExecutionCommandKind.ScheduleActivity,
                            status: RuntimeSchedulerWorkItemResultStatus.Paused,
                            handlerName: nameof(PauseAfterFirstDrainSchedulerDrainer),
                            startedAt: now,
                            completedAt: now,
                            error: "Paused for test.")
                    ]);
            }

            _pauseNextDrain = true;
            var results = new List<RuntimeSchedulerWorkItemResult>();

            while (await queue.DequeueAsync(request.WorkflowExecutionId, cancellationToken) is { } item)
            {
                results.Add(new RuntimeSchedulerWorkItemResult(
                    workItemId: item.WorkItemId,
                    workflowExecutionId: item.WorkflowExecutionId,
                    commandKind: item.CommandKind,
                    status: RuntimeSchedulerWorkItemResultStatus.Completed,
                    handlerName: nameof(PauseAfterFirstDrainSchedulerDrainer),
                    startedAt: now,
                    completedAt: now));
            }

            return new RuntimeSchedulerDrainResult(
                workflowExecutionId: request.WorkflowExecutionId,
                startedAt: now,
                completedAt: now,
                items: results);
        }
    }

    private sealed class EnqueueOncePostCommitOutboxProcessor(
        IWorkflowSchedulerWorkQueue queue,
        RuntimeSchedulerWorkItem workItem) : IRuntimePostCommitOutboxProcessor
    {
        private bool _delivered;
        public List<RuntimePostCommitOutboxProcessRequest> Requests { get; } = [];

        public async ValueTask<RuntimePostCommitOutboxProcessResult> ProcessAsync(
            RuntimePostCommitOutboxProcessRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);

            if (_delivered)
                return new RuntimePostCommitOutboxProcessResult([]);

            _delivered = true;
            await queue.EnqueueAsync(workItem, cancellationToken);
            return new RuntimePostCommitOutboxProcessResult(
            [
                new RuntimePostCommitOutboxProcessedItem(
                    OutboxItemId: "outbox-follow-up",
                    IntentId: "intent-follow-up",
                    RequestedDeliveryResultStatus: RuntimePostCommitOutboxStatus.Delivered,
                    FailureMessage: null)
            ]);
        }
    }

    private sealed class MixedDeliveryPostCommitOutboxProcessor(
        IWorkflowSchedulerWorkQueue queue,
        RuntimeSchedulerWorkItem workItem) : IRuntimePostCommitOutboxProcessor
    {
        private bool _processed;
        public List<RuntimePostCommitOutboxProcessRequest> Requests { get; } = [];

        public async ValueTask<RuntimePostCommitOutboxProcessResult> ProcessAsync(
            RuntimePostCommitOutboxProcessRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);

            if (_processed)
                return new RuntimePostCommitOutboxProcessResult([]);

            _processed = true;
            await queue.EnqueueAsync(workItem, cancellationToken);
            return new RuntimePostCommitOutboxProcessResult(
            [
                new RuntimePostCommitOutboxProcessedItem(
                    OutboxItemId: "outbox-follow-up",
                    IntentId: "intent-follow-up",
                    RequestedDeliveryResultStatus: RuntimePostCommitOutboxStatus.Delivered,
                    FailureMessage: null),
                new RuntimePostCommitOutboxProcessedItem(
                    OutboxItemId: "outbox-failed",
                    IntentId: "intent-failed",
                    RequestedDeliveryResultStatus: RuntimePostCommitOutboxStatus.FailedRetryable,
                    FailureMessage: "Dispatch failed.")
            ]);
        }
    }

    private sealed class FailingPostCommitOutboxProcessor : IRuntimePostCommitOutboxProcessor
    {
        public ValueTask<RuntimePostCommitOutboxProcessResult> ProcessAsync(
            RuntimePostCommitOutboxProcessRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new RuntimePostCommitOutboxProcessResult(
            [
                new RuntimePostCommitOutboxProcessedItem(
                    OutboxItemId: "outbox-failed",
                    IntentId: "intent-failed",
                    RequestedDeliveryResultStatus: RuntimePostCommitOutboxStatus.FailedRetryable,
                    FailureMessage: "Dispatch failed.")
            ]));
    }

    private sealed class CancelingPostCommitOutboxProcessor : IRuntimePostCommitOutboxProcessor
    {
        public static readonly CancelingPostCommitOutboxProcessor Instance = new();

        private CancelingPostCommitOutboxProcessor()
        {
        }

        public ValueTask<RuntimePostCommitOutboxProcessResult> ProcessAsync(
            RuntimePostCommitOutboxProcessRequest request,
            CancellationToken cancellationToken = default) =>
            throw new OperationCanceledException();
    }

    private sealed class AlwaysDeliveringPostCommitOutboxProcessor : IRuntimePostCommitOutboxProcessor
    {
        public static readonly AlwaysDeliveringPostCommitOutboxProcessor Instance = new();

        private AlwaysDeliveringPostCommitOutboxProcessor()
        {
        }

        public ValueTask<RuntimePostCommitOutboxProcessResult> ProcessAsync(
            RuntimePostCommitOutboxProcessRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new RuntimePostCommitOutboxProcessResult(
            [
                new RuntimePostCommitOutboxProcessedItem(
                    OutboxItemId: Guid.NewGuid().ToString("N"),
                    IntentId: Guid.NewGuid().ToString("N"),
                    RequestedDeliveryResultStatus: RuntimePostCommitOutboxStatus.Delivered,
                    FailureMessage: null)
            ]));
    }

    private sealed class EmptyPostCommitOutboxProcessor : IRuntimePostCommitOutboxProcessor
    {
        public static readonly EmptyPostCommitOutboxProcessor Instance = new();

        private EmptyPostCommitOutboxProcessor()
        {
        }

        public ValueTask<RuntimePostCommitOutboxProcessResult> ProcessAsync(
            RuntimePostCommitOutboxProcessRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new RuntimePostCommitOutboxProcessResult([]));
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

    private sealed class AlwaysFaultingSchedulerWorkHandler : IWorkflowSchedulerWorkHandler
    {
        public string Name => nameof(AlwaysFaultingSchedulerWorkHandler);
        public bool CanHandle(RuntimeSchedulerWorkItem workItem) => workItem.CommandKind == WorkflowExecutionCommandKind.RunSchedulerWork;

        public ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException($"Handler crashed for {workItem.WorkItemId}.");
    }
}
