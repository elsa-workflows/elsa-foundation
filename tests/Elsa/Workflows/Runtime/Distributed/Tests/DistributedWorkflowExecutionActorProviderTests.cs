using System.Collections.Concurrent;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Distributed.Options;
using Elsa.Workflows.Runtime.Distributed.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Distributed.Tests;

public sealed class DistributedWorkflowExecutionActorProviderTests
{
    private const string ExecutionId = "wf-1";
    private const string NodeA = "node-a";
    private const string NodeB = "node-b";
    private readonly DateTimeOffset _now = new(2026, 7, 4, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetAgent_WhenNodeOwnsPlacement_DrainsLocally()
    {
        var store = new InMemoryExecutionPlacementStore();
        var transport = new InMemoryExecutionCommandTransport();
        var clock = new MutableTimeProvider(_now);
        var executor = new RecordingCommandExecutor();
        var provider = NewProvider(store, transport, clock, NodeA, executor);

        var actor = await provider.GetAgentAsync(Activation());
        var result = await actor.EnqueueAsync(Envelope("env-1"));

        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted, result.Status);
        Assert.Equal(new[] { "env-1" }, executor.Processed);
        Assert.Equal(0, await transport.CountPendingAsync(ExecutionId));
    }

    [Fact]
    public async Task GetAgent_WhenAnotherNodeOwnsPlacement_ForwardsToTransport()
    {
        var store = new InMemoryExecutionPlacementStore();
        var transport = new InMemoryExecutionCommandTransport();
        var clock = new MutableTimeProvider(_now);

        // Node A takes placement first.
        var executorA = new RecordingCommandExecutor();
        var providerA = NewProvider(store, transport, clock, NodeA, executorA);
        await providerA.GetAgentAsync(Activation());

        // Node B resolves an actor for the same execution: it does not own placement, so it forwards.
        var executorB = new RecordingCommandExecutor();
        var providerB = NewProvider(store, transport, clock, NodeB, executorB);
        var actorB = await providerB.GetAgentAsync(Activation());
        var result = await actorB.EnqueueAsync(Envelope("env-1"));

        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Deferred, result.Status);
        Assert.Equal(NodeA, result.Metadata["runtime.distributed.owningNode"]);
        Assert.Empty(executorB.Processed);
        Assert.Equal(1, await transport.CountPendingAsync(ExecutionId));
    }

    [Fact]
    public async Task Passivate_ReleasesPlacement_SoAnotherNodeCanClaim()
    {
        var store = new InMemoryExecutionPlacementStore();
        var transport = new InMemoryExecutionCommandTransport();
        var clock = new MutableTimeProvider(_now);

        var providerA = NewProvider(store, transport, clock, NodeA, new RecordingCommandExecutor());
        await providerA.GetAgentAsync(Activation());
        await providerA.PassivateAsync(Passivation());

        // After A passivates and releases, node B can now own and drain locally.
        var executorB = new RecordingCommandExecutor();
        var providerB = NewProvider(store, transport, clock, NodeB, executorB);
        var actorB = await providerB.GetAgentAsync(Activation());
        var result = await actorB.EnqueueAsync(Envelope("env-1"));

        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted, result.Status);
        Assert.Equal(new[] { "env-1" }, executorB.Processed);
    }

    [Fact]
    public void Capabilities_AdvertiseDistributedPlacementAndFencing()
    {
        var provider = NewProvider(new InMemoryExecutionPlacementStore(), new InMemoryExecutionCommandTransport(), new MutableTimeProvider(_now), NodeA, new RecordingCommandExecutor());

        Assert.True(provider.Capabilities.HasFlag(WorkflowExecutionActorCapabilities.DistributedPlacement));
        Assert.True(provider.Capabilities.HasFlag(WorkflowExecutionActorCapabilities.LeaseFencing));
        Assert.True(provider.Capabilities.HasFlag(WorkflowExecutionActorCapabilities.Passivation));
    }

    private DistributedWorkflowExecutionActorProvider NewProvider(
        InMemoryExecutionPlacementStore store,
        InMemoryExecutionCommandTransport transport,
        MutableTimeProvider clock,
        string nodeId,
        RecordingCommandExecutor executor)
    {
        var options = new ExecutionPlacementOptions { NodeId = nodeId, LeaseDuration = TimeSpan.FromSeconds(30) };
        var placementService = new ExecutionPlacementService(store, clock, options);
        var local = new InProcessWorkflowExecutionActorProvider(executor);
        return new DistributedWorkflowExecutionActorProvider(local, placementService, transport, clock);
    }

    private static WorkflowExecutionActorActivationRequest Activation() => new(
        workflowExecutionId: ExecutionId,
        reason: WorkflowExecutionActorActivationReason.SchedulerWork,
        requestedAt: DateTimeOffset.UtcNow,
        requestedBy: "test",
        requiredCapabilities: WorkflowExecutionActorCapabilities.None);

    private static WorkflowExecutionActorPassivationRequest Passivation() => new(
        workflowExecutionId: ExecutionId,
        boundary: WorkflowExecutionActorPassivationBoundary.ProviderSafeBoundary,
        requestedAt: DateTimeOffset.UtcNow,
        reason: "test");

    private WorkflowExecutionCommandEnvelope Envelope(string envelopeId)
    {
        var command = new WorkflowExecutionCommand(
            CommandId: $"cmd-{envelopeId}",
            WorkflowExecutionId: ExecutionId,
            Kind: WorkflowExecutionCommandKind.RunSchedulerWork,
            EnqueuedAt: _now,
            Payload: null,
            Metadata: new Dictionary<string, string>());

        return new WorkflowExecutionCommandEnvelope(
            envelopeId: envelopeId,
            workflowExecutionId: ExecutionId,
            command: command,
            idempotencyKey: $"idem-{envelopeId}",
            deliveryMode: WorkflowExecutionCommandDeliveryMode.AtLeastOnce,
            enqueuedAt: _now);
    }

    private sealed class RecordingCommandExecutor : IWorkflowExecutionCommandExecutor
    {
        private readonly ConcurrentQueue<string> _processed = new();

        public IReadOnlyList<string> Processed => _processed.ToArray();

        public ValueTask<WorkflowExecutionCommandProcessResult> ProcessAsync(WorkflowExecutionCommandEnvelope envelope, CancellationToken cancellationToken = default)
        {
            _processed.Enqueue(envelope.EnvelopeId);
            return new ValueTask<WorkflowExecutionCommandProcessResult>(WorkflowExecutionCommandProcessResult.NoDrain);
        }
    }
}
