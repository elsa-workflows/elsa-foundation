using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Distributed.Contracts;
using Elsa.Workflows.Runtime.Distributed.Options;
using Elsa.Workflows.Runtime.Distributed.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Runtime.Distributed.Tests;

/// <summary>
/// One in-process "node" over shared cluster state (placement store + transport + clock). A test constructs two of
/// these with distinct node IDs to exercise routing, ownership hand-off, and failover deterministically.
/// </summary>
internal sealed class NodeHarness
{
    public NodeHarness(
        string nodeId,
        InMemoryExecutionPlacementStore placementStore,
        InMemoryExecutionCommandTransport transport,
        MutableTimeProvider clock,
        IWorkflowExecutionCommandExecutor executor,
        TimeSpan leaseDuration)
    {
        NodeId = nodeId;
        Transport = transport;
        Clock = clock;

        var placementOptions = new ExecutionPlacementOptions { NodeId = nodeId, LeaseDuration = leaseDuration };
        PlacementService = new ExecutionPlacementService(placementStore, clock, placementOptions);

        var local = new InProcessWorkflowExecutionActorProvider(executor);
        Provider = new DistributedWorkflowExecutionActorProvider(local, PlacementService, transport, clock);

        var pumpOptions = new ExecutionPlacementPumpOptions
        {
            SweepInterval = TimeSpan.FromSeconds(10),
            MaxBackoffInterval = TimeSpan.FromMinutes(5),
            MaxExecutionsPerSweep = 100,
            TransportLeaseBatchSize = 100
        };

        Pump = new ExecutionPlacementPumpTask(
            Provider,
            PlacementService,
            transport,
            Microsoft.Extensions.Options.Options.Create(placementOptions),
            Microsoft.Extensions.Options.Options.Create(pumpOptions),
            clock,
            NullLogger<ExecutionPlacementPumpTask>.Instance);
    }

    public string NodeId { get; }
    public IExecutionCommandTransport Transport { get; }
    public MutableTimeProvider Clock { get; }
    public ExecutionPlacementService PlacementService { get; }
    public DistributedWorkflowExecutionActorProvider Provider { get; }
    public ExecutionPlacementPumpTask Pump { get; }

    /// <summary>Resolves an actor for the execution and enqueues the command, as an inbound caller on this node would.</summary>
    public async ValueTask<WorkflowExecutionCommandDispatchResult> DispatchAsync(string executionId, WorkflowExecutionCommandEnvelope envelope)
    {
        var actor = await Provider.GetAgentAsync(new WorkflowExecutionActorActivationRequest(
            workflowExecutionId: executionId,
            reason: WorkflowExecutionActorActivationReason.SchedulerWork,
            requestedAt: Clock.GetUtcNow(),
            requestedBy: NodeId,
            requiredCapabilities: WorkflowExecutionActorCapabilities.None));

        return await actor.EnqueueAsync(envelope);
    }

    public static WorkflowExecutionCommandEnvelope Envelope(string executionId, string envelopeId, DateTimeOffset now)
    {
        var command = new WorkflowExecutionCommand(
            CommandId: $"cmd-{envelopeId}",
            WorkflowExecutionId: executionId,
            Kind: WorkflowExecutionCommandKind.RunSchedulerWork,
            EnqueuedAt: now,
            Payload: null,
            Metadata: new Dictionary<string, string>());

        return new WorkflowExecutionCommandEnvelope(
            envelopeId: envelopeId,
            workflowExecutionId: executionId,
            command: command,
            idempotencyKey: $"idem-{envelopeId}",
            deliveryMode: WorkflowExecutionCommandDeliveryMode.AtLeastOnce,
            enqueuedAt: now);
    }
}
