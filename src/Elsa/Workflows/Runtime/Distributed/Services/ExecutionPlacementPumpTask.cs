using Elsa.Tasks.Core;
using Elsa.Persistence.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Distributed.Contracts;
using Elsa.Workflows.Runtime.Distributed.Options;
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
    private readonly IPersistenceScopeRunner? _scopeRunner;
    private readonly IExecutionPlacementService? _placementService;
    private readonly IExecutionCommandTransport? _transport;
    private readonly WorkflowExecutionPartition? _directPartition;
    private readonly IOptions<ExecutionPlacementOptions> _placementOptions;
    private readonly IOptions<ExecutionPlacementPumpOptions> _pumpOptions;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<ExecutionPlacementPumpTask> _logger;
    private int _consecutiveSweepFailures;

    [ActivatorUtilitiesConstructor]
    public ExecutionPlacementPumpTask(
        IWorkflowExecutionActorProvider actorProvider,
        IPersistenceScopeRunner scopeRunner,
        IOptions<ExecutionPlacementOptions> placementOptions,
        IOptions<ExecutionPlacementPumpOptions> pumpOptions,
        TimeProvider timeProvider,
        ILogger<ExecutionPlacementPumpTask> logger)
    {
        ArgumentNullException.ThrowIfNull(actorProvider);
        ArgumentNullException.ThrowIfNull(scopeRunner);
        ArgumentNullException.ThrowIfNull(placementOptions);
        ArgumentNullException.ThrowIfNull(pumpOptions);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _actorProvider = actorProvider;
        _scopeRunner = scopeRunner;
        _placementOptions = placementOptions;
        _pumpOptions = pumpOptions;
        _timeProvider = timeProvider;
        _logger = logger;
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
    {
        ArgumentNullException.ThrowIfNull(actorProvider);
        ArgumentNullException.ThrowIfNull(placementService);
        ArgumentNullException.ThrowIfNull(transport);
        ArgumentNullException.ThrowIfNull(partition);
        ArgumentNullException.ThrowIfNull(placementOptions);
        ArgumentNullException.ThrowIfNull(pumpOptions);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _actorProvider = actorProvider;
        _placementService = placementService;
        _transport = transport;
        _directPartition = partition;
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
        if (_scopeRunner is null)
            return await SweepAsync(_placementService!, _transport!, _directPartition!, cancellationToken);

        var total = new ExecutionPlacementSweepResult(0, 0, 0, 0);
        await _scopeRunner.RunAsync(async (persistenceScope, operationScope, operationCancellationToken) =>
        {
            var result = await SweepAsync(
                operationScope.ServiceProvider.GetRequiredService<IExecutionPlacementService>(),
                operationScope.ServiceProvider.GetRequiredService<IExecutionCommandTransport>(),
                new WorkflowExecutionPartition(persistenceScope.Value),
                operationCancellationToken);
            total = Add(total, result);
        }, cancellationToken);
        return total;
    }

    private async ValueTask<ExecutionPlacementSweepResult> SweepAsync(
        IExecutionPlacementService placementService,
        IExecutionCommandTransport transport,
        WorkflowExecutionPartition expectedPartition,
        CancellationToken cancellationToken)
    {
        var pumpOptions = _pumpOptions.Value;
        var leaseDuration = _placementOptions.Value.LeaseDuration;
        var now = _timeProvider.GetUtcNow();

        var renewed = 0;
        var claimed = 0;
        var dispatched = 0;
        var acked = 0;

        // 1. Renew placements this node holds so ownership does not lapse mid-drain.
        foreach (var lease in await placementService.ListOwnedAsync(cancellationToken))
        {
            var renewal = await placementService.TryClaimAsync(lease.WorkflowExecutionId, cancellationToken);
            if (renewal.IsOwnedByClaimant)
                renewed++;
        }

        // 2. Discover executions with visible transport backlog, claim any we can own, and drain them locally.
        var pending = await transport.ListPendingExecutionIdsAsync(now, cancellationToken);
        var executionsThisSweep = 0;

        foreach (var executionId in pending)
        {
            if (executionsThisSweep >= pumpOptions.MaxExecutionsPerSweep)
                break;

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
                    if (await transport.AckAsync(executionId, item.TransportItemId, placementService.NodeId, _timeProvider.GetUtcNow(), cancellationToken))
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

        var actor = await _actorProvider.GetAgentAsync(activation, cancellationToken);
        return await actor.EnqueueAsync(envelope, cancellationToken);
    }

    private TimeSpan ComputeInterval()
    {
        var options = _pumpOptions.Value;
        var failures = Volatile.Read(ref _consecutiveSweepFailures);
        return failures <= 0 ? options.SweepInterval : ComputeBackoff(options.SweepInterval, options.MaxBackoffInterval, failures);
    }

    private static ExecutionPlacementSweepResult Add(
        ExecutionPlacementSweepResult left,
        ExecutionPlacementSweepResult right) => new(
        left.RenewedCount + right.RenewedCount,
        left.ClaimedCount + right.ClaimedCount,
        left.DispatchedCommandCount + right.DispatchedCommandCount,
        left.AckedCount + right.AckedCount);

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
