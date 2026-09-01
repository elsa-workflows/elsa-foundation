using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Distributed.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Workflows.Runtime.Distributed.Services;

/// <summary>
/// Clustered <see cref="IWorkflowExecutionActorProvider"/> that adds per-execution placement (routing) and durable
/// cross-node command transport on top of the in-process actor subsystem. It composes an internal
/// <see cref="InProcessWorkflowExecutionActorProvider"/> for local drains and replaces the single active provider
/// registration (constitution S=2.6). Core runtime contracts gain no reference to this leaf (S=2.7).
/// </summary>
/// <remarks>
/// <para>
/// Placement is the routing layer. A per-execution placement lease decides which node runs the drain for a workflow
/// execution, so that under normal operation exactly one node holds the mailbox and commands are serialized through it.
/// Placement is deliberately best-effort: leases expire on a <see cref="TimeProvider"/>-driven clock, and a node that
/// loses the network or dies simply stops renewing, letting a survivor claim the execution. Placement never, by itself,
/// guarantees that two nodes cannot both believe they own the same execution at the same instant — during a
/// lease-handover window they transiently can.
/// </para>
/// <para>
/// Fencing is the safety layer, and it is authoritative. Every drain acquires W5's monotonic execution fencing token
/// from the shared liveness store; the checkpoint committer re-checks that token at commit time and rejects any write
/// whose token is not the highest observed. So even if placement routing is wrong for a window — even if a dead node
/// resurrects mid-drain and reaches its commit — its stale, lower fencing token is rejected and its writes never land.
/// Placement decides where work runs; fencing decides whether a commit is allowed to persist. Double durable execution
/// is prevented by fencing, not by placement, which is why the distributed provider consumes the fencing seam unchanged
/// and adds only routing on top.
/// </para>
/// </remarks>
public sealed class DistributedWorkflowExecutionActorProvider : IWorkflowExecutionActorProvider
{
    public const string ProviderName = nameof(DistributedWorkflowExecutionActorProvider);

    private readonly InProcessWorkflowExecutionActorProvider _localProvider;
    private readonly IPersistenceOperationScopeFactory? _operationScopeFactory;
    private readonly IExecutionPlacementService? _placementService;
    private readonly IExecutionCommandTransport? _transport;
    private readonly TimeProvider _timeProvider;
    private readonly IWorkflowExecutionLeaseFencingCapability? _leaseFencingCapability;

    public DistributedWorkflowExecutionActorProvider(
        InProcessWorkflowExecutionActorProvider localProvider,
        IPersistenceOperationScopeFactory operationScopeFactory,
        TimeProvider timeProvider,
        IWorkflowExecutionLeaseFencingCapability? leaseFencingCapability = null)
    {
        ArgumentNullException.ThrowIfNull(localProvider);
        ArgumentNullException.ThrowIfNull(operationScopeFactory);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _localProvider = localProvider;
        _operationScopeFactory = operationScopeFactory;
        _timeProvider = timeProvider;
        _leaseFencingCapability = leaseFencingCapability;
    }

    public DistributedWorkflowExecutionActorProvider(
        InProcessWorkflowExecutionActorProvider localProvider,
        IExecutionPlacementService placementService,
        IExecutionCommandTransport transport,
        TimeProvider timeProvider,
        IWorkflowExecutionLeaseFencingCapability? leaseFencingCapability = null)
    {
        ArgumentNullException.ThrowIfNull(localProvider);
        ArgumentNullException.ThrowIfNull(placementService);
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _localProvider = localProvider;
        _placementService = placementService;
        _transport = transport;
        _timeProvider = timeProvider;
        _leaseFencingCapability = leaseFencingCapability;
    }

    public WorkflowExecutionActorCapabilities Capabilities
    {
        get
        {
            var capabilities =
                WorkflowExecutionActorCapabilities.InProcessMailbox |
                WorkflowExecutionActorCapabilities.DistributedPlacement |
                WorkflowExecutionActorCapabilities.Passivation;

            return _leaseFencingCapability?.IsAvailable is true
                ? capabilities | WorkflowExecutionActorCapabilities.LeaseFencing
                : capabilities;
        }
    }

    public async ValueTask<IWorkflowExecutionActor> GetAgentAsync(WorkflowExecutionActorActivationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Routing decision: claim placement for this execution. If this node owns it, drain locally through the
        // composed in-process actor; otherwise return a forwarding stub that routes the command to the owning node's
        // durable inbox. Placement is best-effort routing only — the fencing token checked at checkpoint commit is what
        // keeps a transient double-owner window from producing a second durable execution.
        await using var operationScope = await CreateOperationScopeAsync(request.Partition, cancellationToken);
        var placementService = operationScope.PlacementService;
        var claim = await placementService.TryClaimAsync(request.WorkflowExecutionId, cancellationToken);

        if (claim.IsOwnedByClaimant)
            return await _localProvider.GetAgentAsync(request, cancellationToken);

        return new ForwardingWorkflowExecutionActor(
            request.WorkflowExecutionId,
            request.Partition,
            placementService.NodeId,
            claim.Lease.OwnerId,
            _operationScopeFactory,
            _transport,
            _timeProvider);
    }

    public async ValueTask PassivateAsync(WorkflowExecutionActorPassivationRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // Drop the local actor first so no drain is admitted after we relinquish routing, then release placement so a
        // survivor can claim it. Only release a lease this node still holds.
        await _localProvider.PassivateAsync(request, cancellationToken);

        await using var operationScope = await CreateOperationScopeAsync(request.Partition, cancellationToken);
        var placementService = operationScope.PlacementService;
        var lease = await placementService.FindOwnerAsync(request.WorkflowExecutionId, cancellationToken);
        if (lease is not null && string.Equals(lease.OwnerId, placementService.NodeId, StringComparison.Ordinal))
            await placementService.ReleaseAsync(lease, cancellationToken);
    }

    private async ValueTask<PlacementOperationScope> CreateOperationScopeAsync(
        WorkflowExecutionPartition partition,
        CancellationToken cancellationToken)
    {
        if (_operationScopeFactory is null)
            return new PlacementOperationScope(null, _placementService!);

        var scope = await _operationScopeFactory.CreateAsync(new PersistenceScope(partition.Value), cancellationToken);
        return new PlacementOperationScope(
            scope,
            scope.ServiceProvider.GetRequiredService<IExecutionPlacementService>());
    }

    private sealed class PlacementOperationScope(
        PersistenceOperationScope? scope,
        IExecutionPlacementService placementService) : IAsyncDisposable
    {
        public IExecutionPlacementService PlacementService { get; } = placementService;

        public ValueTask DisposeAsync() => scope?.DisposeAsync() ?? ValueTask.CompletedTask;
    }
}
