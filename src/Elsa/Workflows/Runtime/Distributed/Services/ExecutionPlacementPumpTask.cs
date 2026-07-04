using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Distributed.Contracts;
using Elsa.Workflows.Runtime.Distributed.Options;
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
/// This is the failover re-drive loop: when a node dies, its placement lease and its in-flight transport leases both
/// expire on the injected <see cref="TimeProvider"/> clock, so the survivor's sweep claims the execution and re-drives
/// its commands. Re-drive is safe — not merely deduplicated — because the drain acquires a fresh, strictly greater W5
/// fencing token; the dead node's stale token is rejected at checkpoint commit. All cadence and bounds come from
/// options evaluated against <see cref="TimeProvider"/>; there are no wall-clock literals here. A sweep that throws is
/// caught, logged, and never rethrown, and consecutive failures widen the schedule interval geometrically.
/// </remarks>
public sealed class ExecutionPlacementPumpTask : IRecurringTask
{
    private readonly IWorkflowExecutionActorProvider _actorProvider;
    private readonly IExecutionPlacementService _placementService;
    private readonly IExecutionCommandTransport _transport;
    private readonly IOptions<ExecutionPlacementOptions> _placementOptions;
    private readonly IOptions<ExecutionPlacementPumpOptions> _pumpOptions;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ExecutionPlacementPumpTask> _logger;
    private int _consecutiveSweepFailures;

    public ExecutionPlacementPumpTask(
        IWorkflowExecutionActorProvider actorProvider,
        IExecutionPlacementService placementService,
        IExecutionCommandTransport transport,
        IOptions<ExecutionPlacementOptions> placementOptions,
        IOptions<ExecutionPlacementPumpOptions> pumpOptions,
        TimeProvider timeProvider,
        ILogger<ExecutionPlacementPumpTask> logger)
    {
        ArgumentNullException.ThrowIfNull(actorProvider);
        ArgumentNullException.ThrowIfNull(placementService);
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(placementOptions);
        ArgumentNullException.ThrowIfNull(pumpOptions);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _actorProvider = actorProvider;
        _placementService = placementService;
        _transport = transport;
        _placementOptions = placementOptions;
        _pumpOptions = pumpOptions;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>The interval preceding the next sweep, widened geometrically by the consecutive-failure count.</summary>
    public TimeSpan CurrentSweepInterval => ComputeInterval();

    /// <summary>
    /// Runs a single bounded sweep: renew held placements, then claim and drain transport backlog for owned executions.
    /// Public so tests can drive the pump deterministically without the timer.
    /// </summary>
    public async ValueTask<ExecutionPlacementSweepResult> SweepAsync(CancellationToken cancellationToken = default)
    {
        var pumpOptions = _pumpOptions.Value;
        var leaseDuration = _placementOptions.Value.LeaseDuration;
        var now = _timeProvider.GetUtcNow();

        var renewed = 0;
        var claimed = 0;
        var dispatched = 0;
        var acked = 0;

        // 1. Renew placements this node holds so ownership does not lapse mid-drain.
        foreach (var lease in await _placementService.ListOwnedAsync(cancellationToken))
        {
            var renewal = await _placementService.TryClaimAsync(lease.WorkflowExecutionId, cancellationToken);
            if (renewal.IsOwnedByClaimant)
                renewed++;
        }

        // 2. Discover executions with visible transport backlog, claim any we can own, and drain them locally.
        var pending = await _transport.ListPendingExecutionIdsAsync(now, cancellationToken);
        var executionsThisSweep = 0;

        foreach (var executionId in pending)
        {
            if (executionsThisSweep >= pumpOptions.MaxExecutionsPerSweep)
                break;

            var claim = await _placementService.TryClaimAsync(executionId, cancellationToken);
            if (!claim.IsOwnedByClaimant)
                continue;

            claimed++;
            executionsThisSweep++;

            var leased = await _transport.LeaseAsync(executionId, _placementService.NodeId, now, leaseDuration, pumpOptions.TransportLeaseBatchSize, cancellationToken);

            foreach (var item in leased)
            {
                dispatched++;
                var result = await DispatchAsync(executionId, item.Envelope, cancellationToken);

                // Ack only on a delivered outcome. Deferred/Rejected leaves the item leased; when the lease expires it
                // becomes visible again and is re-driven, preserving at-least-once delivery.
                if (result.Status is WorkflowExecutionCommandDispatchStatus.Accepted
                    or WorkflowExecutionCommandDispatchStatus.Duplicate
                    or WorkflowExecutionCommandDispatchStatus.AcceptedButFaulted)
                {
                    if (await _transport.AckAsync(executionId, item.TransportItemId, _placementService.NodeId, _timeProvider.GetUtcNow(), cancellationToken))
                        acked++;
                }
            }
        }

        return new ExecutionPlacementSweepResult(renewed, claimed, dispatched, acked);
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        try
        {
            await SweepAsync(cancellationToken);
            _consecutiveSweepFailures = 0;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            var failures = Interlocked.Increment(ref _consecutiveSweepFailures);
            _logger.LogError(exception, "Placement sweep failed ({ConsecutiveFailures} consecutive); backing off to {Interval}", failures, ComputeInterval());
        }
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public ITaskSchedule GetSchedule() => new AdaptiveIntervalSchedule(() => CurrentSweepInterval, _logger);

    private async ValueTask<WorkflowExecutionCommandDispatchResult> DispatchAsync(string executionId, WorkflowExecutionCommandEnvelope envelope, CancellationToken cancellationToken)
    {
        var activation = new WorkflowExecutionActorActivationRequest(
            workflowExecutionId: executionId,
            reason: WorkflowExecutionActorActivationReason.Recovery,
            requestedAt: _timeProvider.GetUtcNow(),
            requestedBy: _placementService.NodeId,
            requiredCapabilities: WorkflowExecutionActorCapabilities.None);

        var actor = await _actorProvider.GetAgentAsync(activation, cancellationToken);
        return await actor.EnqueueAsync(envelope, cancellationToken);
    }

    private TimeSpan ComputeInterval()
    {
        var options = _pumpOptions.Value;
        var failures = Volatile.Read(ref _consecutiveSweepFailures);
        return failures <= 0 ? options.SweepInterval : ComputeBackoff(options.SweepInterval, options.MaxBackoffInterval, failures);
    }

    // Geometric backoff (base * 2^(failures-1)) clamped to maxInterval, guarded against overflow.
    private static TimeSpan ComputeBackoff(TimeSpan baseInterval, TimeSpan maxInterval, int failures)
    {
        var baseTicks = baseInterval.Ticks;
        var maxTicks = maxInterval.Ticks;

        if (baseTicks <= 0)
            return maxInterval;
        if (baseTicks >= maxTicks)
            return maxInterval;

        var exponent = Math.Min(failures - 1, 30);
        var multiplier = 1L << exponent;

        if (multiplier > maxTicks / baseTicks)
            return maxInterval;

        var scaledTicks = baseTicks * multiplier;
        return scaledTicks >= maxTicks ? maxInterval : TimeSpan.FromTicks(scaledTicks);
    }
}

/// <summary>Per-sweep counters for diagnostics and deterministic tests.</summary>
public sealed record ExecutionPlacementSweepResult(int RenewedCount, int ClaimedCount, int DispatchedCommandCount, int AckedCount)
{
    public bool DidWork => ClaimedCount > 0 || DispatchedCommandCount > 0 || RenewedCount > 0;
}
