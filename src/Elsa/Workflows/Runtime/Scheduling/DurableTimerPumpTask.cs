using System.Collections.Concurrent;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Scheduling.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Runtime.Scheduling;

/// <summary>
/// Recurring background pump that fires due <see cref="DurableTimer"/>s. Each tick runs one bounded sweep:
/// it loads at most <see cref="DurableTimerPumpOptions.MaxTimersPerTick"/> due timers and dispatches each
/// as a bookmark resume through the single-writer <see cref="IBookmarkResumeDispatcher"/> (the same mailbox
/// path every other resume uses). The pump owns two independent backoff mechanisms so failures degrade
/// gracefully, mirroring <c>RuntimeResumptionPumpTask</c>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Whole-sweep backoff.</b> A sweep that throws is caught, logged, and never rethrown (so it cannot
/// crash the host). Consecutive failures widen the schedule interval geometrically up to
/// <see cref="DurableTimerPumpOptions.MaxBackoffInterval"/>; the first clean sweep resets it.
/// </para>
/// <para>
/// <b>Per-timer backoff.</b> A timer whose dispatch faults or is rejected/deferred is parked for a
/// geometrically growing window and skipped on subsequent sweeps until eligible again, so a single poisoned
/// timer cannot occupy a dispatch slot on every tick.
/// </para>
/// <para>
/// <b>Idempotency.</b> A dispatched timer is deleted on <c>Dispatched</c>/<c>Duplicate</c> — the resume is
/// durably enqueued into the scheduler work queue before the dispatcher returns, so deletion cannot lose
/// the resume. <c>NotFound</c> past the grace window (bookmark already consumed by an earlier fire, or the
/// workflow is gone) also deletes the timer; within the grace window it is retried to cover a very short
/// delay racing its own bookmark commit. The bookmark is consumed at most once, so an at-least-once
/// duplicate fire cannot double-resume.
/// </para>
/// </remarks>
public sealed class DurableTimerPumpTask : IRecurringTask
{
    private readonly IDurableTimerStore _timerStore;
    private readonly IBookmarkResumeDispatcher _dispatcher;
    private readonly IOptions<DurableTimerPumpOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DurableTimerPumpTask> _logger;
    private readonly ConcurrentDictionary<TimerKey, TimerBackoff> _timerBackoff = new();
    private int _consecutiveSweepFailures;

    public DurableTimerPumpTask(
        IDurableTimerStore timerStore,
        IBookmarkResumeDispatcher dispatcher,
        IOptions<DurableTimerPumpOptions> options,
        TimeProvider timeProvider,
        ILogger<DurableTimerPumpTask> logger)
    {
        ArgumentNullException.ThrowIfNull(timerStore);
        ArgumentNullException.ThrowIfNull(dispatcher);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        _timerStore = timerStore;
        _dispatcher = dispatcher;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    /// <summary>
    /// The interval that will precede the next sweep: <see cref="DurableTimerPumpOptions.SweepInterval"/>
    /// while healthy, widened geometrically by the current consecutive-failure count up to
    /// <see cref="DurableTimerPumpOptions.MaxBackoffInterval"/>.
    /// </summary>
    public TimeSpan CurrentSweepInterval => ComputeInterval();

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var options = _options.Value;
        var now = _timeProvider.GetUtcNow();

        try
        {
            PruneBackoff(options, now);

            var due = await _timerStore.ListDueAsync(now, options.MaxTimersPerTick, cancellationToken);
            var dispatched = 0;

            foreach (var timer in due)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (IsBackingOff(timer, now))
                    continue;

                if (await FireAsync(timer, options, now, cancellationToken))
                    dispatched++;
            }

            _consecutiveSweepFailures = 0;

            if (dispatched > 0 && _logger.IsEnabled(LogLevel.Debug))
                _logger.LogDebug("Durable timer sweep fired {Dispatched}/{DueCount} due timer(s)", dispatched, due.Count);
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
                "Durable timer sweep failed ({ConsecutiveFailures} consecutive); backing off to {Interval}",
                failures,
                ComputeInterval());
        }
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public ITaskSchedule GetSchedule() => new AdaptiveIntervalSchedule(() => CurrentSweepInterval, _logger);

    // Returns true when the timer was handed to the dispatcher (a real fire), false when it was skipped,
    // kept for retry, or deleted without a dispatch. A per-timer failure never escapes the sweep.
    private async Task<bool> FireAsync(DurableTimer timer, DurableTimerPumpOptions options, DateTimeOffset now, CancellationToken cancellationToken)
    {
        BookmarkResumeDispatchResult result;
        try
        {
            var request = new BookmarkResumeDispatchRequest(
                timer.WorkflowExecutionId,
                timer.StimulusType,
                timer.StimulusHash,
                timer.Input,
                idempotencyKey: $"timer:{timer.TimerId}",
                requestedBy: DurableTimerConstants.PumpRequestedBy);

            result = await _dispatcher.DispatchAsync(request, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            BackOff(timer, options, now);
            _logger.LogError(exception, "Durable timer '{TimerId}' dispatch threw; backing off", timer.TimerId);
            return false;
        }

        switch (result.Status)
        {
            case BookmarkResumeDispatchStatus.Dispatched:
            case BookmarkResumeDispatchStatus.Duplicate:
            // The workflow execution or its executable is gone: the timer is orphaned, nothing to resume.
            case BookmarkResumeDispatchStatus.WorkflowExecutionMissing:
            case BookmarkResumeDispatchStatus.ExecutableMissing:
                await DeleteAsync(timer, cancellationToken);
                return result.Status is BookmarkResumeDispatchStatus.Dispatched or BookmarkResumeDispatchStatus.Duplicate;

            case BookmarkResumeDispatchStatus.NotFound:
                // Past grace: the bookmark was consumed by an earlier fire (or never will match) — delete.
                // Within grace: a very short delay may still be committing its bookmark — keep and retry.
                if (now - timer.DueTime > options.NotFoundGrace)
                    await DeleteAsync(timer, cancellationToken);
                else
                    BackOff(timer, options, now);
                return false;

            default:
                // Rejected / Deferred / Ambiguous / ResumeResolutionFailed: transient or fault — keep the
                // timer and back off so a poisoned timer cannot occupy a dispatch slot every tick.
                BackOff(timer, options, now);
                if (_logger.IsEnabled(LogLevel.Debug))
                    _logger.LogDebug("Durable timer '{TimerId}' dispatch returned {Status}; backing off", timer.TimerId, result.Status);
                return false;
        }
    }

    private async Task DeleteAsync(DurableTimer timer, CancellationToken cancellationToken)
    {
        await _timerStore.DeleteAsync(timer.WorkflowExecutionId, timer.TimerId, cancellationToken);
        _timerBackoff.TryRemove(new TimerKey(timer.WorkflowExecutionId, timer.TimerId), out _);
    }

    private bool IsBackingOff(DurableTimer timer, DateTimeOffset now) =>
        _timerBackoff.TryGetValue(new TimerKey(timer.WorkflowExecutionId, timer.TimerId), out var backoff) &&
        backoff.NextEligibleAt > now;

    private void BackOff(DurableTimer timer, DurableTimerPumpOptions options, DateTimeOffset now)
    {
        var key = new TimerKey(timer.WorkflowExecutionId, timer.TimerId);
        var failures = _timerBackoff.TryGetValue(key, out var current) ? current.Failures + 1 : 1;
        var delay = ComputeBackoff(options.SweepInterval, options.MaxBackoffInterval, failures);
        _timerBackoff[key] = new TimerBackoff(now + delay, failures);
    }

    private void PruneBackoff(DurableTimerPumpOptions options, DateTimeOffset now)
    {
        var pruneBefore = now - options.MaxBackoffInterval;

        foreach (var pair in _timerBackoff)
        {
            // Eligible for a full max-backoff window without reappearing: the timer is gone, drop it so the
            // map stays bounded.
            if (pair.Value.NextEligibleAt <= pruneBefore)
                _timerBackoff.TryRemove(pair.Key, out _);
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

    private readonly record struct TimerKey(string WorkflowExecutionId, string TimerId);

    private readonly record struct TimerBackoff(DateTimeOffset NextEligibleAt, int Failures);
}
