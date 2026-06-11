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
        var provider = new InProcessWorkflowExecutionAgentProvider();
        var first = await provider.GetAgentAsync(NewActivationRequest("wfexec-1"));
        var second = await provider.GetAgentAsync(NewActivationRequest("wfexec-1", WorkflowExecutionAgentActivationReason.SchedulerWork));
        var other = await provider.GetAgentAsync(NewActivationRequest("wfexec-2"));

        Assert.Same(first, second);
        Assert.NotSame(first, other);
        Assert.Equal("wfexec-1", first.Descriptor.WorkflowExecutionId);
        Assert.Equal(InProcessWorkflowExecutionAgentProvider.ProviderName, first.Descriptor.ProviderName);
        Assert.True(first.Descriptor.Capabilities.HasFlag(WorkflowExecutionAgentCapabilities.InProcessMailbox));
    }

    [Fact]
    public async Task GetAgentAsync_RejectsUnsupportedCapabilities()
    {
        var provider = new InProcessWorkflowExecutionAgentProvider();

        await Assert.ThrowsAsync<NotSupportedException>(async () => await provider.GetAgentAsync(NewActivationRequest(
            workflowExecutionId: "wfexec-1",
            requiredCapabilities: WorkflowExecutionAgentCapabilities.DistributedPlacement)));
    }

    [Fact]
    public async Task EnqueueAsync_SerializesAcceptedCommandProcessing()
    {
        var processor = new RecordingCommandProcessor();
        var provider = new InProcessWorkflowExecutionAgentProvider(processor);
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
        var provider = new InProcessWorkflowExecutionAgentProvider(processor);
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
    public async Task EnqueueAsync_EvictsOldProcessedIdempotencyKeysAfterConfiguredLimit()
    {
        var processor = new RecordingCommandProcessor();
        var provider = new InProcessWorkflowExecutionAgentProvider(processor, maxProcessedIdempotencyKeysPerAgent: 2);
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
        var provider = new InProcessWorkflowExecutionAgentProvider();
        var oldAgent = await provider.GetAgentAsync(NewActivationRequest("wfexec-1"));

        await provider.PassivateAsync(new WorkflowExecutionAgentPassivationRequest(
            workflowExecutionId: "wfexec-1",
            boundary: WorkflowExecutionAgentPassivationBoundary.AfterCheckpointCommit,
            requestedAt: _now,
            reason: "Host drain"));

        var oldResult = await oldAgent.EnqueueAsync(NewEnvelope(1));
        var newAgent = await provider.GetAgentAsync(NewActivationRequest("wfexec-1", WorkflowExecutionAgentActivationReason.Recovery));

        Assert.Equal(WorkflowExecutionAgentStatus.Passivated, oldAgent.Descriptor.Status);
        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Deferred, oldResult.Status);
        Assert.NotSame(oldAgent, newAgent);
        Assert.Equal(WorkflowExecutionAgentStatus.Active, newAgent.Descriptor.Status);
    }

    [Fact]
    public async Task PassivateAsync_DoesNotCreateReplacementWhileOldMailboxIsStillProcessing()
    {
        var processor = new BlockingCommandProcessor();
        var provider = new InProcessWorkflowExecutionAgentProvider(processor);
        var oldAgent = await provider.GetAgentAsync(NewActivationRequest("wfexec-1"));
        var enqueueTask = oldAgent.EnqueueAsync(NewEnvelope(1)).AsTask();
        await processor.WaitUntilStartedAsync();

        var passivationTask = provider.PassivateAsync(new WorkflowExecutionAgentPassivationRequest(
            workflowExecutionId: "wfexec-1",
            boundary: WorkflowExecutionAgentPassivationBoundary.AfterCheckpointCommit,
            requestedAt: _now,
            reason: "Host drain")).AsTask();
        await WaitUntilStatusAsync(oldAgent, WorkflowExecutionAgentStatus.Passivating);

        var unrelatedAgent = await provider.GetAgentAsync(NewActivationRequest("wfexec-2", WorkflowExecutionAgentActivationReason.Recovery));
        var activationTask = provider.GetAgentAsync(NewActivationRequest("wfexec-1", WorkflowExecutionAgentActivationReason.Recovery)).AsTask();

        Assert.Equal("wfexec-2", unrelatedAgent.Descriptor.WorkflowExecutionId);
        Assert.False(activationTask.IsCompleted);

        processor.Release();
        var enqueueResult = await enqueueTask;
        await passivationTask;
        var newAgent = await activationTask;

        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted, enqueueResult.Status);
        Assert.Equal(WorkflowExecutionAgentStatus.Passivated, oldAgent.Descriptor.Status);
        Assert.NotSame(oldAgent, newAgent);
        Assert.Equal(WorkflowExecutionAgentStatus.Active, newAgent.Descriptor.Status);
    }

    [Fact]
    public async Task EnqueueAsync_RejectsEnvelopeForDifferentWorkflowExecution()
    {
        var provider = new InProcessWorkflowExecutionAgentProvider();
        var agent = await provider.GetAgentAsync(NewActivationRequest("wfexec-1"));

        var result = await agent.EnqueueAsync(NewEnvelope(1, workflowExecutionId: "wfexec-2"));

        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Rejected, result.Status);
        Assert.Equal("Envelope workflow execution ID does not match this agent.", result.Reason);
    }

    [Fact]
    public void Provider_DoesNotIntroduceActorFrameworkDependencies()
    {
        var runtimeCoreAssembly = typeof(InProcessWorkflowExecutionAgentProvider).Assembly;
        var referencedAssemblies = runtimeCoreAssembly.GetReferencedAssemblies().Select(assembly => assembly.Name).ToArray();

        Assert.DoesNotContain(referencedAssemblies, IsActorFrameworkReference);
    }

    private WorkflowExecutionAgentActivationRequest NewActivationRequest(
        string workflowExecutionId,
        WorkflowExecutionAgentActivationReason reason = WorkflowExecutionAgentActivationReason.Start,
        WorkflowExecutionAgentCapabilities requiredCapabilities = WorkflowExecutionAgentCapabilities.InProcessMailbox) =>
        new(
            workflowExecutionId: workflowExecutionId,
            reason: reason,
            requestedAt: _now,
            requestedBy: "runtime-test",
            requiredCapabilities: requiredCapabilities);

    private WorkflowExecutionCommandEnvelope NewEnvelope(
        int index,
        string workflowExecutionId = "wfexec-1",
        string? idempotencyKey = null)
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
            sequence: index);
    }

    private static bool IsActorFrameworkReference(string? name) =>
        name is not null
        && (name.Contains("Orleans", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Dapr", StringComparison.OrdinalIgnoreCase)
            || name.Contains("Proto.Actor", StringComparison.OrdinalIgnoreCase));

    private static async Task WaitUntilStatusAsync(IWorkflowExecutionAgent agent, WorkflowExecutionAgentStatus expectedStatus)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (agent.Descriptor.Status == expectedStatus)
                return;

            await Task.Delay(10);
        }

        Assert.Equal(expectedStatus, agent.Descriptor.Status);
    }

    private sealed class RecordingCommandProcessor : IWorkflowExecutionCommandProcessor
    {
        private readonly object _syncRoot = new();
        private int _currentConcurrency;

        public List<string> EnvelopeIds { get; } = [];
        public int MaxConcurrency { get; private set; }

        public async ValueTask ProcessAsync(WorkflowExecutionCommandEnvelope envelope, CancellationToken cancellationToken = default)
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
        }
    }

    private sealed class BlockingCommandProcessor : IWorkflowExecutionCommandProcessor
    {
        private readonly TaskCompletionSource _started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask ProcessAsync(WorkflowExecutionCommandEnvelope envelope, CancellationToken cancellationToken = default)
        {
            _started.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
        }

        public Task WaitUntilStartedAsync() => _started.Task;

        public void Release() => _release.TrySetResult();
    }
}
