using System.Collections.Concurrent;
using Elsa.Persistence.Core;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Resumption.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Runtime.Resumption;

/// <summary>
/// Recurring background pump that drives <see cref="IRuntimeResumptionService"/>. Each tick runs one
/// bounded sweep; the pump owns two independent backoff mechanisms so failures degrade gracefully:
/// </summary>
/// <remarks>
/// <para>
/// <b>Whole-sweep backoff.</b> A sweep that throws is caught, logged, and never rethrown (so it cannot
/// crash the host). Consecutive failures widen the schedule interval geometrically up to
/// <see cref="RuntimeResumptionOptions.MaxBackoffInterval"/>; the first clean sweep resets it.
/// </para>
/// <para>
/// <b>Per-execution backoff.</b> An execution whose re-drive faults or is rejected is parked for a
/// geometrically growing window and excluded from subsequent sweeps until eligible again, so a single
/// poisoned execution cannot occupy a re-drive slot on every tick and starve healthy executions out of
/// the per-sweep cap (<see cref="RuntimeResumptionOptions.MaxExecutionsPerSweep"/>).
/// </para>
/// </remarks>
public sealed class RuntimeResumptionPumpTask : IRecurringTask
{
    private static readonly IReadOnlySet<string> EmptyExcluded = new HashSet<string>(StringComparer.Ordinal);

    private static readonly PersistenceScope DirectConstructionScope = new(PersistenceScope.DefaultValue);

    private readonly IPersistenceScopeRunner? _scopeRunner;
    private readonly IRuntimeResumptionService? _resumptionService;
    private readonly IOptions<RuntimeResumptionOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RuntimeResumptionPumpTask> _logger;
    private readonly ConcurrentDictionary<ExecutionBackoffKey, ExecutionBackoff> _executionBackoff = new();
    private int _consecutiveSweepFailures;

    [ActivatorUtilitiesConstructor]
    public RuntimeResumptionPumpTask(
        IPersistenceScopeRunner scopeRunner,
        IOptions<RuntimeResumptionOptions> options,
        TimeProvider timeProvider,
        ILogger<RuntimeResumptionPumpTask> logger)
        : this(options, timeProvider, logger)
    {
        ArgumentNullException.ThrowIfNull(scopeRunner);
        _scopeRunner = scopeRunner;
    }

    /// <summary>Direct-construction seam retained for focused pump tests and custom hosts.</summary>
    public RuntimeResumptionPumpTask(
        IRuntimeResumptionService resumptionService,
        IOptions<RuntimeResumptionOptions> options,
        TimeProvider timeProvider,
        ILogger<RuntimeResumptionPumpTask> logger)
        : this(options, timeProvider, logger)
    {
        ArgumentNullException.ThrowIfNull(resumptionService);

        _resumptionService = resumptionService;
    }

    private RuntimeResumptionPumpTask(
        IOptions<RuntimeResumptionOptions> options,
        TimeProvider timeProvider,
        ILogger<RuntimeResumptionPumpTask> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// The interval that will precede the next sweep: <see cref="RuntimeResumptionOptions.SweepInterval"/>
    /// while healthy, widened geometrically by the current consecutive-failure count up to
    /// <see cref="RuntimeResumptionOptions.MaxBackoffInterval"/>.
    /// </summary>
    public TimeSpan CurrentSweepInterval => ComputeInterval();

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var options = _options.Value;
        var now = _timeProvider.GetUtcNow();

        try
        {
            if (_scopeRunner is null)
            {
                await SweepAsync(DirectConstructionScope, _resumptionService!, options, now, cancellationToken);
            }
            else
            {
                await _scopeRunner.RunAsync(async (persistenceScope, operationScope, operationCancellationToken) =>
                {
                    if (operationScope.ServiceProvider.GetService<IWorkflowTestScopeCleaner>() is { } scopeCleaner)
                        await scopeCleaner.SweepAsync(now, operationCancellationToken);
                    await SweepAsync(
                        persistenceScope,
                        operationScope.ServiceProvider.GetRequiredService<IRuntimeResumptionService>(),
                        options,
                        now,
                        operationCancellationToken);
                }, cancellationToken);
            }
            _consecutiveSweepFailures = 0;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (OutboxProcessingException exception)
        {
            var failures = Interlocked.Increment(ref _consecutiveSweepFailures);
            _logger.LogError(
                new EventId(68107, "RuntimePostCommitResultRecordingFailed"),
                "Runtime resumption sweep could not record a post-commit delivery result. OutboxItemId={OutboxItemId} IntentId={IntentId} ConsecutiveFailures={ConsecutiveFailures} BackoffInterval={BackoffInterval}",
                exception.OutboxItemId,
                exception.IntentId,
                failures,
                ComputeInterval());
        }
        catch (Exception exception)
        {
            var failures = Interlocked.Increment(ref _consecutiveSweepFailures);
            _logger.LogError(
                exception,
                "Runtime resumption sweep failed ({ConsecutiveFailures} consecutive); backing off to {Interval}",
                failures,
                ComputeInterval());
        }
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public ITaskSchedule GetSchedule() => new AdaptiveIntervalSchedule(() => CurrentSweepInterval, _logger);

    private async ValueTask SweepAsync(
        PersistenceScope persistenceScope,
        IRuntimeResumptionService resumptionService,
        RuntimeResumptionOptions options,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var request = new RuntimeResumptionSweepRequest(
            outboxBatchSize: options.OutboxBatchSize,
            backlogBatchSize: options.BacklogBatchSize,
            recoveryScanBatchSize: options.RecoveryScanBatchSize,
            leaseTimeout: options.LeaseTimeout,
            heartbeatTimeout: options.HeartbeatTimeout,
            maxExecutionsPerSweep: options.MaxExecutionsPerSweep,
            excludedWorkflowExecutionIds: SnapshotExcluded(persistenceScope, options, now));
        var result = await resumptionService.SweepAsync(request, cancellationToken);

        ApplyPerExecutionBackoff(persistenceScope, result, options, now);
        if (result.DidWork && _logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug(
                "Resumption sweep delivered {Delivered}/{Attempted} outbox items, re-drove {Dispatches} execution(s), and purged {PurgedItems} residual work item(s) from {PurgedExecutions} terminal execution(s)",
                result.OutboxDeliveredCount,
                result.OutboxAttemptedCount,
                result.Dispatches.Count,
                result.PurgedWorkItemCount,
                result.TerminalExecutionsPurged);
    }

    private IReadOnlySet<string> SnapshotExcluded(
        PersistenceScope persistenceScope,
        RuntimeResumptionOptions options,
        DateTimeOffset now)
    {
        HashSet<string>? excluded = null;
        var pruneBefore = now - options.MaxBackoffInterval;

        foreach (var pair in _executionBackoff)
        {
            if (pair.Key.PersistenceScope == persistenceScope && pair.Value.NextEligibleAt > now)
                (excluded ??= new HashSet<string>(StringComparer.Ordinal)).Add(pair.Key.WorkflowExecutionId);
            else if (pair.Value.NextEligibleAt <= pruneBefore)
                // Eligible for a full max-backoff window without reappearing: the execution is gone, drop it so the map stays bounded.
                _executionBackoff.TryRemove(pair.Key, out _);
        }

        return excluded ?? EmptyExcluded;
    }

    private void ApplyPerExecutionBackoff(
        PersistenceScope persistenceScope,
        RuntimeResumptionSweepResult result,
        RuntimeResumptionOptions options,
        DateTimeOffset now)
    {
        foreach (var dispatch in result.Dispatches)
        {
            var key = new ExecutionBackoffKey(persistenceScope, dispatch.WorkflowExecutionId);
            if (dispatch.Outcome is RuntimeResumptionDispatchOutcome.Faulted or RuntimeResumptionDispatchOutcome.Rejected)
            {
                var failures = _executionBackoff.TryGetValue(key, out var current) ? current.Failures + 1 : 1;
                var delay = ComputeBackoff(options.SweepInterval, options.MaxBackoffInterval, failures);
                _executionBackoff[key] = new ExecutionBackoff(now + delay, failures);
            }
            else
            {
                _executionBackoff.TryRemove(key, out _);
            }
        }
    }

    private TimeSpan ComputeInterval()
    {
        var options = _options.Value;
        var failures = Volatile.Read(ref _consecutiveSweepFailures);
        return failures <= 0
            ? options.SweepInterval
            : ComputeBackoff(options.SweepInterval, options.MaxBackoffInterval, failures);
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

    private readonly record struct ExecutionBackoffKey(PersistenceScope PersistenceScope, string WorkflowExecutionId);

    private readonly record struct ExecutionBackoff(DateTimeOffset NextEligibleAt, int Failures);
}
