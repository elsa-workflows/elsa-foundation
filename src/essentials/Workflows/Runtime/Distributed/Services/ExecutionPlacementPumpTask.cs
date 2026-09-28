using Elsa.Cluster.Core.Exceptions;
using Elsa.Cluster.Core.Models;
using Elsa.Tasks.Core;
using Elsa.Tasks.Schedules;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Distributed.Contracts;
using Elsa.Workflows.Runtime.Distributed.Models;
using Elsa.Workflows.Runtime.Distributed.Options;
using Elsa.Workflows.Runtime.Distributed.Placement;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Runtime.Distributed.Services;

/// <summary>
/// Recurring background pump that keeps this node's placement current and drains the cross-node command transport for
/// executions it owns. Each tick runs one bounded sweep that (1) renews the placement leases this node holds so it keeps
/// ownership while actively draining, and (2) discovers executions with pending transport items, claims placement for
/// any that are unowned or whose owner's lease has expired, leases their commands, dispatches each to the local actor,
/// and acks on success.
/// </summary>
/// <remarks>
/// <para>
/// This is the failover re-drive loop: when a node dies, its placement lease and its in-flight transport leases both
/// expire on the injected <see cref="TimeProvider"/> clock, so the survivor's sweep claims the execution and re-drives
/// its commands. Re-drive is safe — not merely deduplicated — because the drain acquires a fresh, strictly greater
/// fencing token; the dead node's stale token is rejected at checkpoint commit. All cadence and bounds come from
/// options evaluated against <see cref="TimeProvider"/>; there are no wall-clock literals here. A sweep that throws is
/// caught, logged, and never rethrown, and consecutive failures widen the schedule interval geometrically.
/// </para>
/// <para>
/// Renewal is a compare-and-set on the lease as held, and never grants (spec 184, FR-014): a lease that was released
/// or taken since is not taken back, and the execution is passivated locally instead.
/// </para>
/// <para>
/// Composed with <see cref="ExecutionPlacementMembership"/>, as the feature composes it, placement is a membership query
/// (spec 184). Before claiming anything, the pump runs the shell's join sweep once per process (FR-022). It claims and
/// renews only what the gate says this shell can run now (FR-011, FR-012, FR-015, FR-016), and hands off, at the
/// drain's next boundary, what it can no longer run or holds while its member drains (FR-019). An active member that
/// sees a host id departed confirms it with a fresh read and reclaims that host id's leases within the sweep (FR-023).
/// Work no active member can run stays in the transport and is reported (FR-017, FR-018). When the pump stops it hands
/// off everything it still holds (FR-021).
/// </para>
/// </remarks>
public sealed class ExecutionPlacementPumpTask : BackoffSweepPumpTask, IRecurringTask
{
    private readonly IWorkflowExecutionActorProvider _actorProvider;
    private readonly IPersistenceScopeRunner? _scopeRunner;
    private readonly IExecutionPlacementService? _placementService;
    private readonly IExecutionCommandTransport? _transport;
    private readonly WorkflowExecutionPartition? _directPartition;
    private readonly IOptions<ExecutionPlacementOptions> _placementOptions;
    private readonly IOptions<ExecutionPlacementPumpOptions> _pumpOptions;
    private readonly TimeProvider _timeProvider;
    private readonly ExecutionPlacementMembership? _membership;
    private readonly HashSet<ClusterMemberIdentity> _reclaimedDepartures = [];
    private readonly Dictionary<string, BacklogRotation> _rotations = new(StringComparer.Ordinal);

    public ExecutionPlacementPumpTask(
        IWorkflowExecutionActorProvider actorProvider,
        IPersistenceScopeRunner scopeRunner,
        IOptions<ExecutionPlacementOptions> placementOptions,
        IOptions<ExecutionPlacementPumpOptions> pumpOptions,
        TimeProvider timeProvider,
        ILogger<ExecutionPlacementPumpTask> logger)
        : this(actorProvider, scopeRunner, placementOptions, pumpOptions, timeProvider, logger, membership: null)
    {
    }

    /// <summary>The composition the feature uses: placement as a membership query (spec 184).</summary>
    [ActivatorUtilitiesConstructor]
    public ExecutionPlacementPumpTask(
        IWorkflowExecutionActorProvider actorProvider,
        IPersistenceScopeRunner scopeRunner,
        IOptions<ExecutionPlacementOptions> placementOptions,
        IOptions<ExecutionPlacementPumpOptions> pumpOptions,
        TimeProvider timeProvider,
        ILogger<ExecutionPlacementPumpTask> logger,
        ExecutionPlacementMembership? membership)
        : base(logger)
    {
        ArgumentNullException.ThrowIfNull(actorProvider);
        ArgumentNullException.ThrowIfNull(scopeRunner);
        ArgumentNullException.ThrowIfNull(placementOptions);
        ArgumentNullException.ThrowIfNull(pumpOptions);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _actorProvider = actorProvider;
        _scopeRunner = scopeRunner;
        _placementOptions = placementOptions;
        _pumpOptions = pumpOptions;
        _timeProvider = timeProvider;
        _membership = membership;
    }

    public ExecutionPlacementPumpTask(
        IWorkflowExecutionActorProvider actorProvider,
        IExecutionPlacementService placementService,
        IExecutionCommandTransport transport,
        WorkflowExecutionPartition partition,
        IOptions<ExecutionPlacementOptions> placementOptions,
        IOptions<ExecutionPlacementPumpOptions> pumpOptions,
        TimeProvider timeProvider,
        ILogger<ExecutionPlacementPumpTask> logger)
        : base(logger)
    {
        ArgumentNullException.ThrowIfNull(actorProvider);
        ArgumentNullException.ThrowIfNull(placementService);
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(partition);
        ArgumentNullException.ThrowIfNull(placementOptions);
        ArgumentNullException.ThrowIfNull(pumpOptions);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _actorProvider = actorProvider;
        _placementService = placementService;
        _transport = transport;
        _directPartition = partition;
        _placementOptions = placementOptions;
        _pumpOptions = pumpOptions;
        _timeProvider = timeProvider;
    }

    protected override TimeSpan SweepInterval => _pumpOptions.Value.SweepInterval;

    protected override TimeSpan MaxBackoffInterval => _pumpOptions.Value.MaxBackoffInterval;

    /// <summary>
    /// Runs a single bounded sweep: renew held placements, then claim and drain transport backlog for owned executions.
    /// Public so tests can drive the pump deterministically without the timer.
    /// </summary>
    public async ValueTask<ExecutionPlacementSweepResult> SweepOnceAsync(CancellationToken cancellationToken = default)
    {
        if (_scopeRunner is null)
            return await SweepAsync(_placementService!, _transport!, _directPartition!, services: null, cancellationToken);

        var total = ExecutionPlacementSweepResult.Empty;
        if (_membership is not null)
        {
            total = total.Add(await ActivateAsync(cancellationToken));
            total = total.Add(await ReclaimDepartedAsync(cancellationToken));
        }

        await _scopeRunner.RunAsync(async (persistenceScope, operationScope, operationCancellationToken) =>
        {
            var result = await SweepAsync(
                operationScope.ServiceProvider.GetRequiredService<IExecutionPlacementService>(),
                operationScope.ServiceProvider.GetRequiredService<IExecutionCommandTransport>(),
                new WorkflowExecutionPartition(persistenceScope.Value),
                operationScope.ServiceProvider,
                operationCancellationToken);
            total = total.Add(result);
        }, cancellationToken);

        if (_membership is not null)
            await PublishRunnabilityIfChangedAsync(cancellationToken);
        return total;
    }

    /// <summary>
    /// Hands off every execution this member still holds, in every persistence scope (spec 184, FR-021): passivating each
    /// local actor, which waits for an in-flight drain to finish its turn, releasing its placement lease, and making its
    /// unacknowledged transport items visible. A drain that outlasts the lease duration is left as a crash would be; its
    /// leases are reclaimed once the host id is departed.
    /// </summary>
    public async ValueTask<int> HandOffAllAsync(string reason, CancellationToken cancellationToken = default)
    {
        if (_scopeRunner is null)
            return await HandOffOwnedAsync(_placementService!, _transport!, _directPartition!, reason, cancellationToken);

        var handedOff = 0;
        await _scopeRunner.RunAsync(async (persistenceScope, operationScope, operationCancellationToken) =>
            handedOff += await HandOffOwnedAsync(
                operationScope.ServiceProvider.GetRequiredService<IExecutionPlacementService>(),
                operationScope.ServiceProvider.GetRequiredService<IExecutionCommandTransport>(),
                new WorkflowExecutionPartition(persistenceScope.Value),
                reason,
                operationCancellationToken), cancellationToken);
        return handedOff;
    }

    /// <summary>Runs the shell's join sweep and records its runnability entry as soon as the pump starts, so claims wait
    /// for as short a time as they must (FR-022). A failure is logged and retried by the next sweep.</summary>
    async Task IRecurringTask.StartAsync(CancellationToken cancellationToken)
    {
        if (_membership is null || _scopeRunner is null)
            return;

        try
        {
            await ActivateAsync(cancellationToken);
            await PublishRunnabilityIfChangedAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            Logger.LogWarning(exception, "The distributed runtime could not finish activating; the next placement sweep retries.");
        }
    }

    /// <summary>Hands off everything this member holds before it stops (FR-021), within one lease duration, and withdraws
    /// this shell's runnability entry.</summary>
    async Task IRecurringTask.StopAsync(CancellationToken cancellationToken)
    {
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bounded.CancelAfter(_placementOptions.Value.LeaseDuration);
        try
        {
            var handedOff = await HandOffAllAsync("the host is stopping", bounded.Token);
            if (handedOff > 0)
                Logger.LogInformation("Handed off {Count} workflow execution(s) before stopping.", handedOff);
        }
        catch (Exception exception)
        {
            Logger.LogWarning(exception, "Could not hand off every workflow execution before stopping; the rest are reclaimed once this host id is departed, or expire.");
        }

        if (_membership is not null && _membership.Runnability.Remove(_membership.Gate.Shell))
            await TryPublishAsync(CancellationToken.None);
    }

    private async ValueTask<ExecutionPlacementSweepResult> SweepAsync(
        IExecutionPlacementService placementService,
        IExecutionCommandTransport transport,
        WorkflowExecutionPartition expectedPartition,
        IServiceProvider? services,
        CancellationToken cancellationToken)
    {
        var pumpOptions = _pumpOptions.Value;
        var leaseDuration = _placementOptions.Value.LeaseDuration;
        var now = _timeProvider.GetUtcNow();
        var gate = services is null ? null : _membership?.Gate;
        var standing = gate?.CheckStanding();

        // A draining or departed member hands off everything it holds (FR-019, FR-021). A joining member, or one whose
        // join sweep has not completed, neither renews nor hands off: a lease under its host id may still be its
        // predecessor's, which only the join sweep may release (FR-022).
        var handingOff = gate?.Membership.GetLocalStanding().Status is MemberStatus.Draining or MemberStatus.Left;
        var renewing = gate is null || standing is null;

        var renewed = 0;
        var claimed = 0;
        var dispatched = 0;
        var acked = 0;
        var handedOff = 0;

        // 1. Renew placements this node holds so ownership does not lapse mid-drain, or hand them off.
        if (renewing || handingOff)
        {
            foreach (var lease in await placementService.ListOwnedAsync(pumpOptions.MaxExecutionsPerSweep, cancellationToken))
            {
                var decision = handingOff ? standing : gate is null
                    ? null
                    : await gate.DecideAsync(lease.WorkflowExecutionId, services!, [], PendingCommands(transport, lease.WorkflowExecutionId), cancellationToken);
                if (decision is { IsRunnable: false })
                {
                    gate!.Report(lease.WorkflowExecutionId, expectedPartition, decision);
                    await HandOffAsync(lease.WorkflowExecutionId, placementService, transport, expectedPartition, decision.Reason!, cancellationToken);
                    handedOff++;
                    continue;
                }

                if (await placementService.TryRenewAsync(lease, cancellationToken) is not null)
                    renewed++;
                else
                    await PassivateLocallyAsync(lease.WorkflowExecutionId, expectedPartition, cancellationToken);
            }
        }

        // 2. Discover executions with visible transport backlog, claim any we can own, and drain them locally. A member
        //    whose standing forbids claiming claims nothing; work it may not run is left in the transport, unleased.
        var waiting = new Dictionary<string, PlacementDecision>(StringComparer.Ordinal);
        if (standing is null)
        {
            // Work that no member here may run stays pending (FR-018), so a gated pump rotates through the backlog
            // rather than rereading the same first page, which could starve the runnable work behind it.
            var rotation = gate is null ? null : _rotations.TryGetValue(expectedPartition.Value, out var current) ? current : _rotations[expectedPartition.Value] = new BacklogRotation();
            var skip = rotation?.Offset ?? 0;
            var pending = await transport.ListPendingExecutionIdsAsync(
                now,
                pumpOptions.MaxExecutionsPerSweep,
                skip,
                cancellationToken);
            var executionsThisSweep = 0;

            foreach (var executionId in pending)
            {
                if (executionsThisSweep >= pumpOptions.MaxExecutionsPerSweep)
                    break;

                if (gate is not null)
                {
                    var decision = await gate.DecideAsync(executionId, services!, [], PendingCommands(transport, executionId), cancellationToken);
                    gate.Report(executionId, expectedPartition, decision);
                    if (!decision.IsRunnable)
                    {
                        if (decision.IsAboutRequirement)
                            waiting[executionId] = decision;
                        continue;
                    }
                }

                var claim = await placementService.TryClaimAsync(executionId, cancellationToken);
                if (!claim.IsOwnedByClaimant)
                    continue;

                claimed++;
                executionsThisSweep++;

                var leased = await transport.LeaseAsync(executionId, placementService.NodeId, now, leaseDuration, pumpOptions.TransportLeaseBatchSize, cancellationToken);

                foreach (var item in leased)
                {
                    if (item.Envelope.Partition != expectedPartition)
                    {
                        throw new InvalidOperationException(
                            "A persisted command partition does not match the current persistence scope.");
                    }

                    dispatched++;
                    var result = await DispatchAsync(executionId, item.Envelope, placementService, cancellationToken);

                    // Ack only on a delivered outcome. Deferred/Rejected leaves the item leased; when the lease expires it
                    // becomes visible again and is re-driven, preserving at-least-once delivery.
                    if (result.Status is WorkflowExecutionCommandDispatchStatus.Accepted
                        or WorkflowExecutionCommandDispatchStatus.Duplicate
                        or WorkflowExecutionCommandDispatchStatus.AcceptedButFaulted)
                    {
                        if (item.LeaseToken is null)
                            throw new InvalidOperationException("A leased command transport item must carry a lease token.");

                        if (await transport.AckAsync(executionId, item.TransportItemId, placementService.NodeId, item.LeaseToken.Value, _timeProvider.GetUtcNow(), cancellationToken))
                            acked++;
                    }
                }
            }

            if (rotation is not null)
            {
                await ReportUnplaceableAsync(expectedPartition.Value, pending, waiting, cancellationToken);
                rotation.Waiting.UnionWith(waiting.Keys);
                if (pending.Count < pumpOptions.MaxExecutionsPerSweep)
                {
                    // The rotation reached the end of the backlog: whatever it did not find waiting is no longer waiting.
                    _membership!.Unplaceable.Retain(expectedPartition.Value, rotation.Waiting);
                    _rotations[expectedPartition.Value] = new BacklogRotation();
                }
                else
                {
                    rotation.Offset = skip + pending.Count - claimed;
                }
            }
        }

        return new ExecutionPlacementSweepResult(renewed, claimed, dispatched, acked) { HandedOffCount = handedOff };
    }

    /// <summary>
    /// Records, for FR-017, the pending work this member refused for its requirement that no active member satisfies
    /// either, as a cached placement-purpose query shows. Work some active member can run is not unplaceable: that
    /// member's pump claims it.
    /// </summary>
    private async ValueTask ReportUnplaceableAsync(
        string scope,
        IReadOnlyCollection<string> seen,
        IReadOnlyDictionary<string, PlacementDecision> refused,
        CancellationToken cancellationToken)
    {
        var registry = _membership!.Unplaceable;
        foreach (var executionId in seen.Where(executionId => !refused.ContainsKey(executionId)))
            registry.Clear(scope, executionId);

        FleetView? view = null;
        if (refused.Values.Any(decision => decision.Refusal == PlacementRefusalKind.RequirementUnmet))
        {
            try
            {
                view = await _membership.Gate.Membership.ReadFleetAsync(FleetReadMode.Cached, cancellationToken);
            }
            catch (ClusterMembershipException exception)
            {
                Logger.LogDebug(exception, "Could not read the fleet to judge whether refused work is unplaceable; keeping the previous report.");
                return;
            }
        }

        foreach (var (executionId, decision) in refused)
        {
            if (decision.Refusal == PlacementRefusalKind.RequirementUnresolved)
            {
                registry.Record(scope, executionId, decision.Reason!);
                continue;
            }

            var unmet = UnmetByFleet(view!, decision.Requirements);
            if (unmet is null)
                registry.Clear(scope, executionId);
            else
                registry.Record(scope, executionId, unmet);
        }
    }

    /// <summary>
    /// What no active member satisfies of <paramref name="requirements"/>, or <see langword="null"/> when one satisfies all
    /// of them. Each requirement no active member meets on its own is named; when every one is met somewhere but no single
    /// member meets them together, the whole requirement is named.
    /// </summary>
    private static string? UnmetByFleet(FleetView view, IReadOnlyList<RunnabilityRequirement> requirements)
    {
        if (MemberQuery.Placement([.. requirements]).Evaluate(view).Matches.Count > 0)
            return null;

        var unmet = requirements.Where(requirement => MemberQuery.Placement(requirement).Evaluate(view).Matches.Count == 0).ToArray();
        var named = unmet.Length > 0 ? unmet : requirements;
        return $"no active member {string.Join("; ", named.Select(requirement => requirement.ToString()))}";
    }

    /// <summary>
    /// The join sweep (FR-022): the first time this shell's distributed runtime activates in the process, it reclaims
    /// every per-execution lease held under the process's host id, which can only be a predecessor's, before it claims
    /// anything. A shell reload or a rejoin after a lapse is not a first activation.
    /// </summary>
    private async ValueTask<ExecutionPlacementSweepResult> ActivateAsync(CancellationToken cancellationToken)
    {
        // Only a member that has joined is guaranteed to be the one live process under its host id (spec 183, FR-004b):
        // until its status is active, a lease under the host id may still be a live predecessor's.
        var gate = _membership!.Gate;
        if (gate.HasCompletedJoinSweep || gate.Membership.GetLocalStanding().Status != MemberStatus.Active)
            return ExecutionPlacementSweepResult.Empty;

        var hostId = gate.HostId;
        var acquiredAtOrBefore = gate.Ledger.Begin(hostId, _timeProvider.GetUtcNow());
        var result = await ReclaimEverywhereAsync(hostId, acquiredAtOrBefore, HostIdReclaimKind.JoinSweep, cancellationToken);
        gate.Ledger.Complete(hostId, gate.Shell.Name);
        return ExecutionPlacementSweepResult.Empty with { ReclaimedCount = Total(result) };
    }

    /// <summary>
    /// Departure-triggered reclaim (FR-023, FR-026): an active member that has not lapsed looks for departed host ids in
    /// a cached read, confirms each with a fresh read, and reclaims it in every persistence scope. Only positive evidence
    /// counts: a host id absent from the view, one with a live incarnation, and one departed only in the cached read are
    /// never reclaimed from.
    /// </summary>
    private async ValueTask<ExecutionPlacementSweepResult> ReclaimDepartedAsync(CancellationToken cancellationToken)
    {
        var membership = _membership!.Gate.Membership;
        var standing = membership.GetLocalStanding();
        if (standing.Status != MemberStatus.Active || standing.HasLapsed)
            return ExecutionPlacementSweepResult.Empty;

        FleetView fresh;
        try
        {
            var cached = await membership.ReadFleetAsync(FleetReadMode.Cached, cancellationToken);
            var seenDeparted = ExecutionPlacementMembership.Departed(cached, standing.Identity.HostId)
                .Where(identity => !_reclaimedDepartures.Contains(identity))
                .ToArray();
            if (seenDeparted.Length == 0)
                return ExecutionPlacementSweepResult.Empty;

            fresh = await membership.ReadFleetAsync(FleetReadMode.Fresh, cancellationToken);
        }
        catch (ClusterMembershipException exception)
        {
            Logger.LogDebug(exception, "Could not read the fleet; no host id is judged departed until a fresh read succeeds.");
            return ExecutionPlacementSweepResult.Empty;
        }

        var reclaimed = 0;
        foreach (var departed in ExecutionPlacementMembership.Departed(fresh, standing.Identity.HostId).Where(identity => !_reclaimedDepartures.Contains(identity)))
        {
            var result = await ReclaimEverywhereAsync(departed.HostId, fresh.JudgedAt, HostIdReclaimKind.Departure, cancellationToken);
            _reclaimedDepartures.Add(departed);
            reclaimed += Total(result);
        }

        _reclaimedDepartures.IntersectWith(fresh.Members.Select(member => member.Identity));
        return ExecutionPlacementSweepResult.Empty with { ReclaimedCount = reclaimed };
    }

    private async ValueTask<HostIdReclaimResult> ReclaimEverywhereAsync(
        string hostId,
        DateTimeOffset acquiredAtOrBefore,
        HostIdReclaimKind kind,
        CancellationToken cancellationToken)
    {
        var total = HostIdReclaimResult.None;
        await _scopeRunner!.RunAsync(async (persistenceScope, operationScope, operationCancellationToken) =>
            total = total.Add(await _membership!.Reclaimer.ReclaimAsync(
                operationScope.ServiceProvider,
                persistenceScope.Value,
                hostId,
                acquiredAtOrBefore,
                _placementOptions.Value.LeaseDuration,
                kind,
                operationCancellationToken)), cancellationToken);

        Logger.LogInformation(
            "Reclaimed the leases held under host id {HostId} ({Kind}): {PlacementLeases} placement lease(s) released, {TransportItemLeases} transport item lease(s) made visible, {RecoveryCandidates} execution(s) made recovery candidates.",
            hostId,
            kind == HostIdReclaimKind.JoinSweep ? "join sweep" : "departure",
            total.PlacementLeases,
            total.TransportItemLeases,
            total.RecoveryCandidates);
        return total;
    }

    private async ValueTask PublishRunnabilityIfChangedAsync(CancellationToken cancellationToken)
    {
        var entry = await _membership!.ComputeRunnabilityAsync();
        if (_membership.Runnability.Record(_membership.Gate.Shell, entry))
            await TryPublishAsync(cancellationToken);
    }

    private async ValueTask TryPublishAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _membership!.Gate.Membership.PublishReportAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The section is a diagnostic for other members; the next heartbeat or change publishes it (FR-008).
            Logger.LogWarning(exception, "Could not publish this host's runnability report; it is published with the next change or heartbeat.");
        }
    }

    private async ValueTask<int> HandOffOwnedAsync(
        IExecutionPlacementService placementService,
        IExecutionCommandTransport transport,
        WorkflowExecutionPartition partition,
        string reason,
        CancellationToken cancellationToken)
    {
        var handedOff = 0;
        foreach (var lease in await placementService.ListOwnedAsync(DistributedRuntimeQueryLimits.MaximumTake, cancellationToken))
        {
            await HandOffAsync(lease.WorkflowExecutionId, placementService, transport, partition, reason, cancellationToken);
            handedOff++;
        }

        return handedOff;
    }

    /// <summary>
    /// Hands one execution off (FR-019): passivates the local actor at the drain's next boundary, which releases the
    /// placement lease still held under this host id and its placement token, then makes visible every transport item
    /// this member leased for the execution and has not acknowledged, so an active member can claim and lease them.
    /// </summary>
    private async ValueTask HandOffAsync(
        string workflowExecutionId,
        IExecutionPlacementService placementService,
        IExecutionCommandTransport transport,
        WorkflowExecutionPartition partition,
        string reason,
        CancellationToken cancellationToken)
    {
        await _actorProvider.PassivateAsync(
            new WorkflowExecutionActorPassivationRequest(
                workflowExecutionId: workflowExecutionId,
                boundary: WorkflowExecutionActorPassivationBoundary.HostDrain,
                requestedAt: _timeProvider.GetUtcNow(),
                reason: $"placement hand-off: {reason}",
                partition: partition),
            cancellationToken);

        var now = _timeProvider.GetUtcNow();
        foreach (var item in await transport.ListLeasedAsync(placementService.NodeId, now, _pumpOptions.Value.TransportLeaseBatchSize, workflowExecutionId, cancellationToken))
            await transport.ReleaseLeaseAsync(workflowExecutionId, item.TransportItemId, placementService.NodeId, item.LeaseToken!.Value, now, cancellationToken);
    }

    /// <summary>A renewal that found the lease released or taken passivates the local actor without releasing
    /// anything: the lease is no longer this member's (FR-014).</summary>
    private ValueTask PassivateLocallyAsync(string workflowExecutionId, WorkflowExecutionPartition partition, CancellationToken cancellationToken) =>
        _actorProvider.PassivateAsync(
            new WorkflowExecutionActorPassivationRequest(
                workflowExecutionId: workflowExecutionId,
                boundary: WorkflowExecutionActorPassivationBoundary.ProviderSafeBoundary,
                requestedAt: _timeProvider.GetUtcNow(),
                reason: "placement lease no longer held",
                partition: partition),
            cancellationToken);

    private Func<CancellationToken, ValueTask<IReadOnlyList<WorkflowExecutionCommandEnvelope>>> PendingCommands(
        IExecutionCommandTransport transport,
        string workflowExecutionId) =>
        async cancellationToken => (await transport.PeekAsync(workflowExecutionId, _timeProvider.GetUtcNow(), _pumpOptions.Value.TransportLeaseBatchSize, cancellationToken))
            .Select(item => item.Envelope)
            .ToArray();

    protected override async Task SweepAsync(CancellationToken cancellationToken) => await SweepOnceAsync(cancellationToken);

    protected override void OnSweepFailed(Exception exception, int consecutiveFailures, TimeSpan backoffInterval) =>
        Logger.LogError(exception, "Placement sweep failed ({ConsecutiveFailures} consecutive); backing off to {Interval}", consecutiveFailures, backoffInterval);

    private async ValueTask<WorkflowExecutionCommandDispatchResult> DispatchAsync(
        string executionId,
        WorkflowExecutionCommandEnvelope envelope,
        IExecutionPlacementService placementService,
        CancellationToken cancellationToken)
    {
        var activation = new WorkflowExecutionActorActivationRequest(
            workflowExecutionId: executionId,
            reason: WorkflowExecutionActorActivationReason.Recovery,
            requestedAt: _timeProvider.GetUtcNow(),
            requestedBy: placementService.NodeId,
            requiredCapabilities: WorkflowExecutionActorCapabilities.None,
            partition: envelope.Partition);

        // A gated pump decided and claimed before it leased these commands, so they go to the local mailbox directly;
        // deciding again could forward a leased command a second time if the member's standing changed in between.
        var actor = _membership is not null && _actorProvider is DistributedWorkflowExecutionActorProvider distributed
            ? await distributed.GetClaimedLocalAgentAsync(activation, cancellationToken)
            : await _actorProvider.GetAgentAsync(activation, cancellationToken);
        return await actor.EnqueueAsync(envelope, cancellationToken);
    }

    /// <summary>Where a gated pump is in its pass through one scope's backlog, and what it found waiting so far.</summary>
    private sealed class BacklogRotation
    {
        public int Offset { get; set; }

        public HashSet<string> Waiting { get; } = new(StringComparer.Ordinal);
    }

    private static int Total(HostIdReclaimResult result) => result.PlacementLeases + result.TransportItemLeases + result.RecoveryCandidates;
}

/// <summary>Per-sweep counters for diagnostics and deterministic tests.</summary>
public sealed record ExecutionPlacementSweepResult(int RenewedCount, int ClaimedCount, int DispatchedCommandCount, int AckedCount)
{
    public static ExecutionPlacementSweepResult Empty { get; } = new(0, 0, 0, 0);

    /// <summary>Executions handed off because this member could no longer run them or was draining (spec 184, FR-019).</summary>
    public int HandedOffCount { get; init; }

    /// <summary>Leases of every kind this sweep reclaimed, by a join sweep or a departure (spec 184, FR-028).</summary>
    public int ReclaimedCount { get; init; }

    public bool DidWork => ClaimedCount > 0 || DispatchedCommandCount > 0 || RenewedCount > 0 || HandedOffCount > 0 || ReclaimedCount > 0;

    public ExecutionPlacementSweepResult Add(ExecutionPlacementSweepResult other) =>
        new(RenewedCount + other.RenewedCount, ClaimedCount + other.ClaimedCount, DispatchedCommandCount + other.DispatchedCommandCount, AckedCount + other.AckedCount)
        {
            HandedOffCount = HandedOffCount + other.HandedOffCount,
            ReclaimedCount = ReclaimedCount + other.ReclaimedCount
        };
}
