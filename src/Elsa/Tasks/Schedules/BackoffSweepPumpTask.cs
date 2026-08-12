using Elsa.Tasks.Core;
using Microsoft.Extensions.Logging;

namespace Elsa.Tasks.Schedules;

/// <summary>
/// Recurring background pump that runs one bounded sweep per tick and owns the whole-sweep failure
/// backoff every pump previously re-implemented: a sweep that throws is caught, reported through
/// <see cref="OnSweepFailed"/>, and never rethrown (so it cannot crash the host); consecutive failures
/// widen the schedule interval geometrically up to <see cref="MaxBackoffInterval"/>, and the first
/// clean sweep resets it. Derivations own the sweep itself, its scope plumbing, and any per-item
/// backoff (for which <see cref="ComputeBackoff"/> is shared).
/// </summary>
public abstract class BackoffSweepPumpTask : IRecurringTask
{
    private int _consecutiveSweepFailures;

    protected BackoffSweepPumpTask(ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);
        Logger = logger;
    }

    protected ILogger Logger { get; }

    /// <summary>Baseline interval between sweeps while healthy.</summary>
    protected abstract TimeSpan SweepInterval { get; }

    /// <summary>Ceiling the failure backoff widens toward.</summary>
    protected abstract TimeSpan MaxBackoffInterval { get; }

    /// <summary>
    /// The interval that will precede the next sweep: <see cref="SweepInterval"/> while healthy, widened
    /// geometrically by the current consecutive-failure count up to <see cref="MaxBackoffInterval"/>.
    /// </summary>
    public TimeSpan CurrentSweepInterval
    {
        get
        {
            var failures = Volatile.Read(ref _consecutiveSweepFailures);
            return failures <= 0
                ? SweepInterval
                : ComputeBackoff(SweepInterval, MaxBackoffInterval, failures);
        }
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        try
        {
            await SweepAsync(cancellationToken);
            _consecutiveSweepFailures = 0;
        }
        catch (OperationCanceledException exception) when (ShouldRethrowCancellation(exception, cancellationToken))
        {
            throw;
        }
        catch (Exception exception) when (IsHandledSweepException(exception))
        {
            var failures = Interlocked.Increment(ref _consecutiveSweepFailures);
            OnSweepFailed(exception, failures, CurrentSweepInterval);
        }
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public ITaskSchedule GetSchedule() => new AdaptiveIntervalSchedule(() => CurrentSweepInterval, Logger);

    /// <summary>Runs one bounded sweep. Cancellation propagates; any other exception feeds the backoff.</summary>
    protected abstract Task SweepAsync(CancellationToken cancellationToken);

    /// <summary>Logs one failed sweep. The failure count and widened interval are already computed.</summary>
    protected abstract void OnSweepFailed(Exception exception, int consecutiveFailures, TimeSpan backoffInterval);

    /// <summary>Which exceptions feed the backoff instead of escaping. Everything by default.</summary>
    protected virtual bool IsHandledSweepException(Exception exception) => true;

    /// <summary>
    /// Whether a cancellation escapes the pump. Every <see cref="OperationCanceledException"/> by
    /// default; a pump whose sweep calls dependencies that surface unrelated cancellations (timeouts)
    /// can narrow this to its own token, letting foreign cancellations feed the backoff instead.
    /// </summary>
    protected virtual bool ShouldRethrowCancellation(OperationCanceledException exception, CancellationToken cancellationToken) => true;

    // Geometric backoff (base * 2^(failures-1)) clamped to maxInterval, guarded against overflow.
    protected static TimeSpan ComputeBackoff(TimeSpan baseInterval, TimeSpan maxInterval, int failures)
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
