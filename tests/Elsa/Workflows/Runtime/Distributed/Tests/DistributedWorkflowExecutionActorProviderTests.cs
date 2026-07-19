using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Distributed.Contracts;
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
    public async Task ForwardingActor_DropsDispatchOptions_SoAmbientServicesNeverCrossProcess()
    {
        // Spec 089 E-D4 / FR-021 (never-cross-process guarantee): the forwarding actor's options overload delegates to
        // the single-arg overload, so request-affine ambient services are dropped by construction at the transport
        // boundary. The command is durably enqueued for the owning node carrying only the envelope — no service
        // provider is (or structurally could be) persisted.
        var store = new InMemoryExecutionPlacementStore();
        var transport = new InMemoryExecutionCommandTransport();
        var clock = new MutableTimeProvider(_now);

        // Node A owns placement; node B resolves a forwarding actor for the same execution.
        var providerA = NewProvider(store, transport, clock, NodeA, new RecordingCommandExecutor());
        await providerA.GetAgentAsync(Activation());
        var providerB = NewProvider(store, transport, clock, NodeB, new RecordingCommandExecutor());
        var forwardingActor = await providerB.GetAgentAsync(Activation());

        // Dispatch WITH ambient services through the options overload — the path a sync-mode HTTP endpoint takes.
        var ambientServices = new RecordingServiceProvider();
        var result = await forwardingActor.EnqueueAsync(
            Envelope("env-1"),
            new WorkflowExecutionCommandDispatchOptions(ambientServices));

        // Deferred: no live write happened on this node — the middleware degrades to 202 without a locality check.
        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Deferred, result.Status);
        Assert.False(ambientServices.WasResolvedFrom, "The forwarding actor must not touch ambient services.");

        // The durably enqueued item carries only the envelope; nothing on it (or its command) exposes a service provider.
        var leased = await transport.LeaseAsync(ExecutionId, NodeA, _now, TimeSpan.FromSeconds(30), maxItems: 10);
        var item = Assert.Single(leased);
        Assert.DoesNotContain(
            item.Envelope.GetType().GetProperties(),
            property => typeof(IServiceProvider).IsAssignableFrom(property.PropertyType));
        Assert.DoesNotContain(
            item.Envelope.Command.GetType().GetProperties(),
            property => typeof(IServiceProvider).IsAssignableFrom(property.PropertyType));
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
    public void In_memory_capabilities_do_not_claim_provider_admitted_lease_fencing()
    {
        var provider = NewProvider(new InMemoryExecutionPlacementStore(), new InMemoryExecutionCommandTransport(), new MutableTimeProvider(_now), NodeA, new RecordingCommandExecutor());

        Assert.True(provider.Capabilities.HasFlag(WorkflowExecutionActorCapabilities.DistributedPlacement));
        Assert.False(provider.Capabilities.HasFlag(WorkflowExecutionActorCapabilities.LeaseFencing));
        Assert.True(provider.Capabilities.HasFlag(WorkflowExecutionActorCapabilities.Passivation));
    }

    [Fact]
    public void Capabilities_advertise_lease_fencing_only_when_the_persistence_path_admits_it()
    {
        var provider = NewProvider(
            new InMemoryExecutionPlacementStore(),
            new InMemoryExecutionCommandTransport(),
            new MutableTimeProvider(_now),
            NodeA,
            new RecordingCommandExecutor(),
            new LeaseFencingCapability());

        Assert.True(provider.Capabilities.HasFlag(WorkflowExecutionActorCapabilities.LeaseFencing));
    }

    private DistributedWorkflowExecutionActorProvider NewProvider(
        InMemoryExecutionPlacementStore store,
        InMemoryExecutionCommandTransport transport,
        MutableTimeProvider clock,
        string nodeId,
        RecordingCommandExecutor executor,
        IWorkflowExecutionLeaseFencingCapability? leaseFencingCapability = null)
    {
        var options = new ExecutionPlacementOptions { NodeId = nodeId, LeaseDuration = TimeSpan.FromSeconds(30) };
        var placementService = new ExecutionPlacementService(store, clock, options);
        var local = new InProcessWorkflowExecutionActorProvider(executor);
        return new DistributedWorkflowExecutionActorProvider(local, placementService, transport, clock, leaseFencingCapability);
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

    private sealed class RecordingServiceProvider : IServiceProvider
    {
        public bool WasResolvedFrom { get; private set; }

        public object? GetService(Type serviceType)
        {
            WasResolvedFrom = true;
            return null;
        }
    }

    private sealed class LeaseFencingCapability : IWorkflowExecutionLeaseFencingCapability
    {
        public bool IsAvailable => true;
    }

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
}
