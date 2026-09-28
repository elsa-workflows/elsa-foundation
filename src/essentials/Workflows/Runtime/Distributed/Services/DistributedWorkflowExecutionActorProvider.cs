using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Distributed.Contracts;
using Elsa.Workflows.Runtime.Distributed.Placement;
using Elsa.Workflows.Runtime.Services.Executions;
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
/// Fencing is the safety layer, and it is authoritative. Every drain acquires a monotonic execution fencing token
/// from the shared liveness store; the checkpoint committer re-checks that token at commit time and rejects any write
/// whose token is not the highest observed. So even if placement routing is wrong for a window — even if a dead node
/// resurrects mid-drain and reaches its commit — its stale, lower fencing token is rejected and its writes never land.
/// Placement decides where work runs; fencing decides whether a commit is allowed to persist. Double durable execution
/// is prevented by fencing, not by placement, which is why the distributed provider consumes the fencing seam unchanged
/// and adds only routing on top.
/// </para>
/// <para>
/// Composed with an <see cref="ExecutionPlacementGate"/>, as the feature composes it, placement is version-aware
/// (spec 184): the claim is decided when the command is enqueued, because a start command carries the pin of an
/// execution that has no durable state yet. The member claims only when the gate says its runtime can run the
/// execution now (FR-011, FR-012); otherwise the command is forwarded to the durable transport with no other side
/// effect, and the dispatch result says why it did not run here (FR-013).
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
    private readonly ExecutionPlacementGate? _gate;

    public DistributedWorkflowExecutionActorProvider(
        InProcessWorkflowExecutionActorProvider localProvider,
        IPersistenceOperationScopeFactory operationScopeFactory,
        TimeProvider timeProvider,
        IWorkflowExecutionLeaseFencingCapability? leaseFencingCapability = null)
        : this(localProvider, operationScopeFactory, timeProvider, leaseFencingCapability, gate: null)
    {
    }

    /// <summary>The composition the feature uses: placement gated by membership and this shell's registries (spec 184).</summary>
    public DistributedWorkflowExecutionActorProvider(
        InProcessWorkflowExecutionActorProvider localProvider,
        IPersistenceOperationScopeFactory operationScopeFactory,
        TimeProvider timeProvider,
        IWorkflowExecutionLeaseFencingCapability? leaseFencingCapability,
        ExecutionPlacementGate? gate)
    {
        ArgumentNullException.ThrowIfNull(localProvider);
        ArgumentNullException.ThrowIfNull(operationScopeFactory);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _localProvider = localProvider;
        _operationScopeFactory = operationScopeFactory;
        _timeProvider = timeProvider;
        _leaseFencingCapability = leaseFencingCapability;
        _gate = gate;
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
        var unsupportedCapabilities = request.RequiredCapabilities & ~Capabilities;
        if (unsupportedCapabilities != WorkflowExecutionActorCapabilities.None)
            throw new NotSupportedException($"The distributed workflow execution actor provider does not support required capabilities: {unsupportedCapabilities}.");

        // A gated claim needs the command in hand: a start command pins an execution that has no durable state yet.
        if (_gate is not null)
            return new PlacementDecidingActor(this, request);

        // Routing decision: claim placement for this execution. If this node owns it, drain locally through the
        // composed in-process actor; otherwise return a forwarding stub that routes the command to the owning node's
        // durable inbox. Placement is best-effort routing only — the fencing token checked at checkpoint commit is what
        // keeps a transient double-owner window from producing a second durable execution.
        await using var operationScope = await CreateOperationScopeAsync(request.Partition, cancellationToken);
        var placementService = operationScope.PlacementService;
        var claim = await placementService.TryClaimAsync(request.WorkflowExecutionId, cancellationToken);

        if (claim.IsOwnedByClaimant)
            return await GetLocalAgentAsync(request, cancellationToken);

        return Forward(request, placementService.NodeId, claim.Lease.OwnerId);
    }

    /// <summary>
    /// The local mailbox for an execution the placement pump has already decided on and claimed in this sweep: the gate
    /// and the claim are not repeated for the commands it leased.
    /// </summary>
    internal ValueTask<IWorkflowExecutionActor> GetClaimedLocalAgentAsync(WorkflowExecutionActorActivationRequest request, CancellationToken cancellationToken) =>
        GetLocalAgentAsync(request, cancellationToken);

    /// <summary>
    /// The composed in-process mailbox for an execution this member owns. The capabilities only this provider adds,
    /// distributed placement and lease fencing, are this provider's to satisfy, so the local mailbox is asked only for
    /// its own; asking it for more would refuse every re-drive that requires this provider's capabilities, as the
    /// recovery sweep's does.
    /// </summary>
    private ValueTask<IWorkflowExecutionActor> GetLocalAgentAsync(WorkflowExecutionActorActivationRequest request, CancellationToken cancellationToken)
    {
        var localCapabilities = request.RequiredCapabilities & _localProvider.Capabilities;
        return _localProvider.GetAgentAsync(
            localCapabilities == request.RequiredCapabilities
                ? request
                : new WorkflowExecutionActorActivationRequest(
                    request.WorkflowExecutionId,
                    request.Reason,
                    request.RequestedAt,
                    request.RequestedBy,
                    localCapabilities,
                    request.Metadata,
                    request.Partition),
            cancellationToken);
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

    /// <summary>
    /// Passivates this member's local actor only, and releases nothing: for the placement pump, when a renewal found the
    /// lease no longer as it held it (spec 184, FR-014). <see cref="PassivateAsync"/> releases whatever lease this host id
    /// holds now, which during a reload can be the one a newer generation of the shell just renewed.
    /// </summary>
    internal ValueTask PassivateLocalActorAsync(WorkflowExecutionActorPassivationRequest request, CancellationToken cancellationToken) =>
        _localProvider.PassivateAsync(request, cancellationToken);

    /// <summary>
    /// Decides placement for one command: the gate first, in an operation scope that is closed before the drain runs,
    /// then the claim. Nothing is written when the gate refuses (FR-013).
    /// </summary>
    private async ValueTask<IWorkflowExecutionActor> DecideAsync(
        WorkflowExecutionActorActivationRequest request,
        WorkflowExecutionCommandEnvelope envelope,
        CancellationToken cancellationToken)
    {
        await using (var operationScope = await CreateOperationScopeAsync(request.Partition, cancellationToken))
        {
            var placementService = operationScope.PlacementService;
            var decision = await _gate!.DecideAsync(request.WorkflowExecutionId, operationScope.Services, [envelope], cancellationToken: cancellationToken);
            _gate.Report(request.WorkflowExecutionId, request.Partition, decision);
            if (!decision.IsRunnable)
                return ForwardingWorkflowExecutionActor.Refused(request.WorkflowExecutionId, request.Partition, placementService.NodeId, decision, _operationScopeFactory, _transport, _timeProvider);

            var claim = await placementService.TryClaimAsync(request.WorkflowExecutionId, cancellationToken);
            if (!claim.IsOwnedByClaimant)
                return Forward(request, placementService.NodeId, claim.Lease.OwnerId);
        }

        return await GetLocalAgentAsync(request, cancellationToken);
    }

    private ForwardingWorkflowExecutionActor Forward(WorkflowExecutionActorActivationRequest request, string localNodeId, string owningNodeId) =>
        new(request.WorkflowExecutionId, request.Partition, localNodeId, owningNodeId, _operationScopeFactory, _transport, _timeProvider);

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

        public IServiceProvider Services => scope?.ServiceProvider ?? throw new InvalidOperationException("A gated placement decision needs an operation scope.");

        public ValueTask DisposeAsync() => scope?.DisposeAsync() ?? ValueTask.CompletedTask;
    }

    /// <summary>
    /// The actor a gated activation returns. It decides placement when the command arrives, then behaves exactly as the
    /// actor that decision selects: the local mailbox when this member claimed the execution, a forwarding stub
    /// otherwise.
    /// </summary>
    private sealed class PlacementDecidingActor(
        DistributedWorkflowExecutionActorProvider provider,
        WorkflowExecutionActorActivationRequest request) : IWorkflowExecutionActor
    {
        private IWorkflowExecutionActor? _decided;

        public WorkflowExecutionActorDescriptor Descriptor => _decided?.Descriptor ?? new(
            workflowExecutionId: request.WorkflowExecutionId,
            agentId: $"distributed-placement:{request.WorkflowExecutionId}",
            providerName: ProviderName,
            status: WorkflowExecutionActorStatus.Unavailable,
            capabilities: WorkflowExecutionActorCapabilities.DistributedPlacement,
            activatedAt: request.RequestedAt,
            metadata: new Dictionary<string, string> { ["runtime.distributed.placement"] = "undecided" });

        public ValueTask<WorkflowExecutionCommandDispatchResult> EnqueueAsync(WorkflowExecutionCommandEnvelope envelope, CancellationToken cancellationToken = default) =>
            EnqueueAsync(envelope, WorkflowExecutionCommandDispatchOptions.Default, cancellationToken);

        public async ValueTask<WorkflowExecutionCommandDispatchResult> EnqueueAsync(
            WorkflowExecutionCommandEnvelope envelope,
            WorkflowExecutionCommandDispatchOptions options,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(envelope);
            ArgumentNullException.ThrowIfNull(options);
            if (!string.Equals(envelope.WorkflowExecutionId, request.WorkflowExecutionId, StringComparison.Ordinal) || envelope.Partition != request.Partition)
            {
                return new WorkflowExecutionCommandDispatchResult(
                    envelopeId: envelope.EnvelopeId,
                    workflowExecutionId: envelope.WorkflowExecutionId,
                    status: WorkflowExecutionCommandDispatchStatus.Rejected,
                    recordedAt: provider._timeProvider.GetUtcNow(),
                    reason: "Envelope workflow execution ID or partition does not match this activation.");
            }

            var actor = await provider.DecideAsync(request, envelope, cancellationToken);
            _decided = actor;

            // A forwarding stub drops the options by construction: request-affine services never cross the transport.
            return await actor.EnqueueAsync(envelope, options, cancellationToken);
        }
    }
}
