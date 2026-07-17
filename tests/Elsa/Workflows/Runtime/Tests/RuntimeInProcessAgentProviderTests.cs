using System.Collections;
using System.Reflection;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeInProcessAgentProviderTests
{
    private readonly DateTimeOffset _now = new(2026, 6, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetAgentAsync_ReturnsOneActiveAgentPerWorkflowExecutionId()
    {
        var provider = new InProcessWorkflowExecutionActorProvider();
        var first = await provider.GetAgentAsync(NewActivationRequest("wfexec-1"));
        var second = await provider.GetAgentAsync(NewActivationRequest("wfexec-1", WorkflowExecutionActorActivationReason.SchedulerWork));
        var other = await provider.GetAgentAsync(NewActivationRequest("wfexec-2"));

        Assert.Same(first, second);
        Assert.NotSame(first, other);
        Assert.Equal("wfexec-1", first.Descriptor.WorkflowExecutionId);
        Assert.Equal(InProcessWorkflowExecutionActorProvider.ProviderName, first.Descriptor.ProviderName);
        Assert.True(first.Descriptor.Capabilities.HasFlag(WorkflowExecutionActorCapabilities.InProcessMailbox));
    }

    [Fact]
    public async Task GetAgentAsync_RejectsUnsupportedCapabilities()
    {
        var provider = new InProcessWorkflowExecutionActorProvider();

        await Assert.ThrowsAsync<NotSupportedException>(async () => await provider.GetAgentAsync(NewActivationRequest(
            workflowExecutionId: "wfexec-1",
            requiredCapabilities: WorkflowExecutionActorCapabilities.DistributedPlacement)));
    }

    [Fact]
    public async Task EnqueueAsync_SerializesAcceptedCommandProcessing()
    {
        var processor = new RecordingCommandProcessor();
        var provider = new InProcessWorkflowExecutionActorProvider(processor);
        var agent = await provider.GetAgentAsync(NewActivationRequest("wfexec-1"));

        var tasks = Enumerable.Range(1, 8)
            .Select(index => agent.EnqueueAsync(NewEnvelope(index)).AsTask())
            .ToArray();

        var results = await Task.WhenAll(tasks);

        Assert.All(results, result => Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted, result.Status));
        Assert.Equal(8, processor.EnvelopeIds.Count);
        Assert.Equal(1, processor.MaxConcurrency);
    }

    [Fact]
    public async Task EnqueueAsync_DeduplicatesProcessedIdempotencyKeys()
    {
        var processor = new RecordingCommandProcessor();
        var provider = new InProcessWorkflowExecutionActorProvider(processor);
        var agent = await provider.GetAgentAsync(NewActivationRequest("wfexec-1"));
        var envelope = NewEnvelope(1, idempotencyKey: "wfexec-1:duplicate");

        var first = await agent.EnqueueAsync(envelope);
        var second = await agent.EnqueueAsync(NewEnvelope(2, idempotencyKey: "wfexec-1:duplicate"));

        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted, first.Status);
        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Duplicate, second.Status);
        Assert.Equal("Idempotency key was already processed.", second.Reason);
        Assert.Single(processor.EnvelopeIds);
    }

    [Fact]
    public async Task Equal_execution_and_idempotency_ids_in_different_partitions_use_independent_actors()
    {
        var processor = new RecordingCommandProcessor();
        var provider = new InProcessWorkflowExecutionActorProvider(processor);
        var tenantA = new WorkflowExecutionPartition("tenant-a");
        var tenantB = new WorkflowExecutionPartition("tenant-b");
        var first = await provider.GetAgentAsync(NewActivationRequest("wfexec-1", partition: tenantA));
        var second = await provider.GetAgentAsync(NewActivationRequest("wfexec-1", partition: tenantB));

        var firstResult = await first.EnqueueAsync(NewEnvelope(1, idempotencyKey: "same-key", partition: tenantA));
        var secondResult = await second.EnqueueAsync(NewEnvelope(2, idempotencyKey: "same-key", partition: tenantB));

        Assert.NotSame(first, second);
        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted, firstResult.Status);
        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted, secondResult.Status);
        Assert.Equal(2, processor.EnvelopeIds.Count);
    }

    [Fact]
    public async Task EnqueueAsync_RejectsEnvelopeForDifferentPartitionWithoutMutatingIdempotencyState()
    {
        var processor = new RecordingCommandProcessor();
        var provider = new InProcessWorkflowExecutionActorProvider(processor);
        var tenantA = new WorkflowExecutionPartition("tenant-a");
        var tenantB = new WorkflowExecutionPartition("tenant-b");
        var agent = await provider.GetAgentAsync(NewActivationRequest("wfexec-1", partition: tenantA));

        var rejected = await agent.EnqueueAsync(NewEnvelope(1, idempotencyKey: "same-key", partition: tenantB));
        var accepted = await agent.EnqueueAsync(NewEnvelope(2, idempotencyKey: "same-key", partition: tenantA));

        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Rejected, rejected.Status);
        Assert.Equal("Envelope persistence scope does not match this agent.", rejected.Reason);
        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted, accepted.Status);
        Assert.Single(processor.EnvelopeIds);
    }

    [Fact]
    public async Task EnqueueAsync_EvictsOldProcessedIdempotencyKeysAfterConfiguredLimit()
    {
        var processor = new RecordingCommandProcessor();
        var provider = new InProcessWorkflowExecutionActorProvider(processor, maxProcessedIdempotencyKeysPerAgent: 2);
        var agent = await provider.GetAgentAsync(NewActivationRequest("wfexec-1"));

        await agent.EnqueueAsync(NewEnvelope(1, idempotencyKey: "key-1"));
        await agent.EnqueueAsync(NewEnvelope(2, idempotencyKey: "key-2"));
        await agent.EnqueueAsync(NewEnvelope(3, idempotencyKey: "key-3"));
        var result = await agent.EnqueueAsync(NewEnvelope(4, idempotencyKey: "key-1"));

        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted, result.Status);
        Assert.Equal(4, processor.EnvelopeIds.Count);
    }

    [Fact]
    public async Task PassivateAsync_RemovesActiveAgentAndDefersOldAgentWork()
    {
        var provider = new InProcessWorkflowExecutionActorProvider();
        var oldAgent = await provider.GetAgentAsync(NewActivationRequest("wfexec-1"));

        await provider.PassivateAsync(NewPassivationRequest("wfexec-1"));

        var oldResult = await oldAgent.EnqueueAsync(NewEnvelope(1));
        var newAgent = await provider.GetAgentAsync(NewActivationRequest("wfexec-1", WorkflowExecutionActorActivationReason.Recovery));

        Assert.Equal(WorkflowExecutionActorStatus.Passivated, oldAgent.Descriptor.Status);
        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Deferred, oldResult.Status);
        Assert.NotSame(oldAgent, newAgent);
        Assert.Equal(WorkflowExecutionActorStatus.Active, newAgent.Descriptor.Status);
    }

    [Fact]
    public async Task PassivateAsync_DoesNotCreateReplacementWhileOldMailboxIsStillProcessing()
    {
        var processor = new BlockingCommandProcessor();
        var provider = new InProcessWorkflowExecutionActorProvider(processor);
        var oldAgent = await provider.GetAgentAsync(NewActivationRequest("wfexec-1"));
        var enqueueTask = oldAgent.EnqueueAsync(NewEnvelope(1)).AsTask();
        await processor.WaitUntilStartedAsync();

        var passivationTask = provider.PassivateAsync(NewPassivationRequest("wfexec-1")).AsTask();
        await WaitUntilStatusAsync(oldAgent, WorkflowExecutionActorStatus.Passivating);

        var unrelatedAgent = await provider.GetAgentAsync(NewActivationRequest("wfexec-2", WorkflowExecutionActorActivationReason.Recovery));
        var activationTask = provider.GetAgentAsync(NewActivationRequest("wfexec-1", WorkflowExecutionActorActivationReason.Recovery)).AsTask();

        Assert.Equal("wfexec-2", unrelatedAgent.Descriptor.WorkflowExecutionId);
        Assert.False(activationTask.IsCompleted);

        processor.Release();
        var enqueueResult = await enqueueTask;
        await passivationTask;
        var newAgent = await activationTask;

        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted, enqueueResult.Status);
        Assert.Equal(WorkflowExecutionActorStatus.Passivated, oldAgent.Descriptor.Status);
        Assert.NotSame(oldAgent, newAgent);
        Assert.Equal(WorkflowExecutionActorStatus.Active, newAgent.Descriptor.Status);
    }

    [Fact]
    public async Task ActivateThenPassivate_ForManyDistinctIds_LeavesLifecycleLockTableEmpty()
    {
        var provider = new InProcessWorkflowExecutionActorProvider();

        // Regression for the unbounded-growth leak: without cleanup, _lifecycleLocks would retain one entry per
        // distinct execution id ever activated. After a full activate -> passivate cycle each entry must be released.
        const int distinctExecutions = 500;
        for (var index = 0; index < distinctExecutions; index++)
        {
            var workflowExecutionId = $"wfexec-{index}";
            await provider.GetAgentAsync(NewActivationRequest(workflowExecutionId));
            await provider.PassivateAsync(NewPassivationRequest(workflowExecutionId));
        }

        Assert.Equal(0, LifecycleLockCount(provider));
    }

    [Fact]
    public async Task ActivateRacingPassivate_ForSameId_DoesNotThrowDeadlockOrLeakLocks()
    {
        var provider = new InProcessWorkflowExecutionActorProvider();
        const string workflowExecutionId = "wfexec-race";

        // Repeatedly race a passivation against a re-activation for the same id. The verify-after-acquire retry loop
        // must keep them serialized on the canonical lock without disposing an instance out from under a waiter.
        for (var round = 0; round < 250; round++)
        {
            await provider.GetAgentAsync(NewActivationRequest(workflowExecutionId));

            var passivate = Task.Run(() => provider.PassivateAsync(NewPassivationRequest(workflowExecutionId)).AsTask());
            var activate = Task.Run(() => provider.GetAgentAsync(
                NewActivationRequest(workflowExecutionId, WorkflowExecutionActorActivationReason.Recovery)).AsTask());

            // WaitAsync surfaces a deadlock as a timeout rather than hanging the test run; await surfaces any throw.
            await Task.WhenAll(passivate, activate).WaitAsync(TimeSpan.FromSeconds(30));
        }

        // Only a single id is ever used, so a live-but-uncleaned table would hold at most one entry; the leak fix keeps
        // it from accumulating stale, orphaned locks from the racing rounds.
        Assert.True(LifecycleLockCount(provider) <= 1);
    }

    [Fact]
    public async Task EnqueueAsync_RejectsEnvelopeForDifferentWorkflowExecution()
    {
        var provider = new InProcessWorkflowExecutionActorProvider();
        var agent = await provider.GetAgentAsync(NewActivationRequest("wfexec-1"));

        var result = await agent.EnqueueAsync(NewEnvelope(1, workflowExecutionId: "wfexec-2"));

        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Rejected, result.Status);
        Assert.Equal("Envelope workflow execution ID does not match this agent.", result.Reason);
    }

    [Fact]
    public void Provider_DoesNotIntroduceActorFrameworkDependencies()
    {
        var runtimeCoreAssembly = typeof(InProcessWorkflowExecutionActorProvider).Assembly;
        var referencedAssemblies = runtimeCoreAssembly.GetReferencedAssemblies().Select(assembly => assembly.Name).ToArray();

        Assert.DoesNotContain(referencedAssemblies, IsActorFrameworkReference);
    }

    private WorkflowExecutionActorActivationRequest NewActivationRequest(
        string workflowExecutionId,
        WorkflowExecutionActorActivationReason reason = WorkflowExecutionActorActivationReason.Start,
        WorkflowExecutionActorCapabilities requiredCapabilities = WorkflowExecutionActorCapabilities.InProcessMailbox,
        WorkflowExecutionPartition? partition = null) =>
        new(
            workflowExecutionId: workflowExecutionId,
            reason: reason,
            requestedAt: _now,
            requestedBy: "runtime-test",
            requiredCapabilities: requiredCapabilities,
            partition: partition);

    private WorkflowExecutionActorPassivationRequest NewPassivationRequest(
        string workflowExecutionId,
        WorkflowExecutionActorPassivationBoundary boundary = WorkflowExecutionActorPassivationBoundary.AfterCheckpointCommit,
        string reason = "Host drain",
        WorkflowExecutionPartition? partition = null) =>
        new(
            workflowExecutionId: workflowExecutionId,
            boundary: boundary,
            requestedAt: _now,
            reason: reason,
            partition: partition);

    // The provider keeps its lifecycle-lock table private (public sealed type, no InternalsVisibleTo), so the
    // growth-regression assertions read its size via reflection rather than a test-only public surface.
    private static int LifecycleLockCount(InProcessWorkflowExecutionActorProvider provider)
    {
        var field = typeof(InProcessWorkflowExecutionActorProvider)
            .GetField("_lifecycleLocks", BindingFlags.Instance | BindingFlags.NonPublic);
        var locks = (IDictionary)field!.GetValue(provider)!;
        return locks.Count;
    }

    private WorkflowExecutionCommandEnvelope NewEnvelope(
        int index,
        string workflowExecutionId = "wfexec-1",
        string? idempotencyKey = null,
        WorkflowExecutionPartition? partition = null)
    {
        using var document = JsonDocument.Parse("""{"workItemId":"work-1"}""");
        var command = new WorkflowExecutionCommand(
            CommandId: $"command-{index}",
            WorkflowExecutionId: workflowExecutionId,
            Kind: WorkflowExecutionCommandKind.RunSchedulerWork,
            EnqueuedAt: _now,
            Payload: document.RootElement.Clone(),
            Metadata: new Dictionary<string, string>());

        return new(
            envelopeId: $"envelope-{index}",
            workflowExecutionId: workflowExecutionId,
            command: command,
            idempotencyKey: idempotencyKey ?? $"{workflowExecutionId}:command-{index}",
            deliveryMode: WorkflowExecutionCommandDeliveryMode.AtLeastOnce,
            enqueuedAt: _now,
            sequence: index,
            partition: partition);
    }

    private static bool IsActorFrameworkReference(string? name) =>
        name is not null
        && (name.Contains("Orleans", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Dapr", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Proto.Actor", StringComparison.OrdinalIgnoreCase));

    private static async Task WaitUntilStatusAsync(IWorkflowExecutionActor agent, WorkflowExecutionActorStatus expectedStatus)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (agent.Descriptor.Status == expectedStatus)
                return;

            await Task.Delay(10);
        }

        Assert.Equal(expectedStatus, agent.Descriptor.Status);
    }

    private sealed class RecordingCommandProcessor : IWorkflowExecutionCommandExecutor
    {
        private readonly object _syncRoot = new();
        private int _currentConcurrency;

        public List<string> EnvelopeIds { get; } = [];
        public int MaxConcurrency { get; private set; }

        public async ValueTask<WorkflowExecutionCommandProcessResult> ProcessAsync(WorkflowExecutionCommandEnvelope envelope, CancellationToken cancellationToken = default)
        {
            lock (_syncRoot)
            {
                _currentConcurrency++;
                MaxConcurrency = Math.Max(MaxConcurrency, _currentConcurrency);
            }

            try
            {
                await Task.Delay(10, cancellationToken);

                lock (_syncRoot)
                    EnvelopeIds.Add(envelope.EnvelopeId);
            }
            finally
            {
                lock (_syncRoot)
                    _currentConcurrency--;
            }

            return WorkflowExecutionCommandProcessResult.NoDrain;
        }
    }

    private sealed class BlockingCommandProcessor : IWorkflowExecutionCommandExecutor
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<WorkflowExecutionCommandProcessResult> ProcessAsync(WorkflowExecutionCommandEnvelope envelope, CancellationToken cancellationToken = default)
        {
            _started.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
            return WorkflowExecutionCommandProcessResult.NoDrain;
        }

        public Task WaitUntilStartedAsync() => _started.Task;

        public void Release() => _release.TrySetResult();
    }
}
