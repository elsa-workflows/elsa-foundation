using Elsa.Tasks.Core;
using Elsa.Persistence.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Scheduling.Options;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Runtime.Scheduling;

/// <summary>
/// Recurring background pump that fires due <see cref="RecurringTriggerSchedule"/>s (W16). Each tick runs one
/// bounded sweep: it loads at most <see cref="RecurringTriggerPumpOptions.MaxSchedulesPerTick"/> due schedules
/// and, for each, dispatches the schedule's start stimulus through <see cref="IStimulusRouter"/> in
/// <see cref="StimulusRoutingMode.StartOnly"/> mode — starting a new workflow instance with no execution id,
/// the piece the resume-oriented durable-timer pump cannot do.
/// </summary>
/// <remarks>
/// <para>
/// <b>Missed-occurrence policy — fire at most once, then advance.</b> A schedule is due when its
/// <see cref="RecurringTriggerSchedule.NextOccurrence"/> is at or before the wake instant. The pump does NOT
/// replay the backlog of occurrences that elapsed while it was down: it advances the cursor to the first
/// occurrence strictly after <i>now</i> (via <see cref="IRecurringScheduleCalculator"/>) and fires exactly once.
/// </para>
/// <para>
/// <b>Claim-first (single fire, W20-ready).</b> The cursor is advanced through the store's compare-and-swap
/// (<see cref="IRecurringTriggerScheduleStore.TryAdvanceAsync"/>) <i>before</i> the start is dispatched, so at
/// most one worker fires a given occurrence even if several sweep concurrently — the single-node realization of
/// the cluster-safe claim a future distributed store (W20) keeps. A crash between the claim and the dispatch
/// loses that one fire, which is the accepted at-most-once trade: the next occurrence still fires, and the
/// backlog is never replayed.
/// </para>
/// <para>
/// <b>Whole-sweep backoff.</b> A sweep that throws is caught, logged, and never rethrown; consecutive failures
/// widen the schedule interval geometrically up to <see cref="RecurringTriggerPumpOptions.MaxBackoffInterval"/>,
/// and the first clean sweep resets it — mirroring the durable-timer pump.
/// </para>
/// </remarks>
public sealed class RecurringTriggerPumpTask : IRecurringTask
{
    private const string PumpRequestedBy = "runtime.recurring-trigger";

    private readonly IPersistenceScopeRunner? _scopeRunner;
    private readonly IRecurringTriggerScheduleStore? _store;
    private readonly IStimulusRouter? _router;
    private readonly IRecurringScheduleCalculator? _calculator;
    private readonly IOptions<RecurringTriggerPumpOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RecurringTriggerPumpTask> _logger;
    private int _consecutiveSweepFailures;

    [ActivatorUtilitiesConstructor]
    public RecurringTriggerPumpTask(
        IPersistenceScopeRunner scopeRunner,
        IOptions<RecurringTriggerPumpOptions> options,
        TimeProvider timeProvider,
        ILogger<RecurringTriggerPumpTask> logger)
        : this(options, timeProvider, logger)
    {
        ArgumentNullException.ThrowIfNull(scopeRunner);
        _scopeRunner = scopeRunner;
    }

    /// <summary>Direct-construction seam retained for focused pump tests and custom hosts.</summary>
    public RecurringTriggerPumpTask(
        IRecurringTriggerScheduleStore store,
        IStimulusRouter router,
        IRecurringScheduleCalculator calculator,
        IOptions<RecurringTriggerPumpOptions> options,
        TimeProvider timeProvider,
        ILogger<RecurringTriggerPumpTask> logger)
        : this(options, timeProvider, logger)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(router);
        ArgumentNullException.ThrowIfNull(calculator);

        _store = store;
        _router = router;
        _calculator = calculator;
    }

    private RecurringTriggerPumpTask(
        IOptions<RecurringTriggerPumpOptions> options,
        TimeProvider timeProvider,
        ILogger<RecurringTriggerPumpTask> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>The interval that will precede the next sweep, widened geometrically under sustained failure.</summary>
    public TimeSpan CurrentSweepInterval => ComputeInterval();

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var options = _options.Value;
        var now = _timeProvider.GetUtcNow();

        try
        {
            if (_scopeRunner is null)
            {
                await SweepAsync(_store!, _router!, _calculator!, options, now, cancellationToken);
            }
            else
            {
                await _scopeRunner.RunAsync(async (_, operationScope, operationCancellationToken) =>
                {
                    await SweepAsync(
                        operationScope.ServiceProvider.GetRequiredService<IRecurringTriggerScheduleStore>(),
                        operationScope.ServiceProvider.GetRequiredService<IStimulusRouter>(),
                        operationScope.ServiceProvider.GetRequiredService<IRecurringScheduleCalculator>(),
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
        catch (Exception exception)
        {
            var failures = Interlocked.Increment(ref _consecutiveSweepFailures);
            _logger.LogError(
                exception,
                "Recurring-trigger sweep failed ({ConsecutiveFailures} consecutive); backing off to {Interval}",
                failures,
                ComputeInterval());
        }
    }

    private async Task SweepAsync(
        IRecurringTriggerScheduleStore store,
        IStimulusRouter router,
        IRecurringScheduleCalculator calculator,
        RecurringTriggerPumpOptions options,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var due = await store.ListDueAsync(now, options.MaxSchedulesPerTick, cancellationToken);
        var fired = 0;

        foreach (var schedule in due)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (await FireAsync(store, router, calculator, schedule, now, cancellationToken))
                fired++;
        }

        if (fired > 0 && _logger.IsEnabled(LogLevel.Debug))
            _logger.LogDebug("Recurring-trigger sweep fired {Fired}/{DueCount} due schedule(s)", fired, due.Count);
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public ITaskSchedule GetSchedule() => new AdaptiveIntervalSchedule(() => CurrentSweepInterval, _logger);

    // Returns true when the schedule's occurrence was claimed and a start dispatched. A per-schedule failure
    // never escapes the sweep.
    private async Task<bool> FireAsync(
        IRecurringTriggerScheduleStore store,
        IStimulusRouter router,
        IRecurringScheduleCalculator calculator,
        RecurringTriggerSchedule schedule,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        DateTimeOffset? next;
        try
        {
            next = calculator.ComputeNext(schedule.Kind, schedule.Expression, now);
        }
        catch (Exception exception)
        {
            // A schedule whose expression no longer parses cannot advance; drop it so it does not jam the sweep.
            _logger.LogError(exception, "Recurring schedule '{ScheduleId}' has an invalid expression; deleting it", schedule.ScheduleId);
            await store.DeleteAsync(schedule.ScheduleId, cancellationToken);
            return false;
        }

        if (next is null)
        {
            // Cron exhausted (no future occurrence): remove the schedule rather than leave it perpetually due.
            await store.DeleteAsync(schedule.ScheduleId, cancellationToken);
            return false;
        }

        // Claim-first: advance the cursor via CAS before dispatching, so a concurrent sweep cannot double-fire
        // this occurrence.
        var claimed = await store.TryAdvanceAsync(schedule.ScheduleId, schedule.NextOccurrence, next.Value, cancellationToken);
        if (!claimed)
            return false;

        try
        {
            var request = new StimulusDispatchRequest(
                stimulusType: schedule.StimulusType,
                stimulusHash: schedule.StimulusHash,
                mode: StimulusRoutingMode.StartOnly,
                idempotencyKey: $"recurring:{schedule.ScheduleId}:{schedule.NextOccurrence.UtcTicks}",
                requestedBy: PumpRequestedBy);

            await router.RouteAsync(request, cancellationToken);
            return true;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            // The occurrence was already claimed (cursor advanced), so this fire is lost by the at-most-once
            // contract; the next occurrence will still fire. Log and move on rather than replay.
            _logger.LogError(exception, "Recurring schedule '{ScheduleId}' start dispatch threw after claim; occurrence dropped", schedule.ScheduleId);
            return false;
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
}
