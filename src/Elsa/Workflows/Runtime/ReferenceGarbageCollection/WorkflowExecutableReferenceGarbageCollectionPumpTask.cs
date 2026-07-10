using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.ReferenceGarbageCollection.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Runtime.ReferenceGarbageCollection;

/// <summary>
/// Recurring background pump that drives <see cref="IWorkflowExecutableReferenceGarbageCollector"/> (ADR 0040). Each
/// tick runs one sweep. A sweep that throws is caught, logged and never rethrown (so it cannot crash the host);
/// consecutive failures widen the schedule interval geometrically up to
/// <see cref="WorkflowExecutableReferenceGarbageCollectionOptions.MaxBackoffInterval"/>, and the first clean sweep
/// resets it — mirroring the resumption and timer pumps.
/// </summary>
public sealed class WorkflowExecutableReferenceGarbageCollectionPumpTask : IRecurringTask
{
    private readonly IWorkflowExecutableReferenceGarbageCollector _garbageCollector;
    private readonly IOptions<WorkflowExecutableReferenceGarbageCollectionOptions> _options;
    private readonly ILogger<WorkflowExecutableReferenceGarbageCollectionPumpTask> _logger;
    private int _consecutiveSweepFailures;

    public WorkflowExecutableReferenceGarbageCollectionPumpTask(
        IWorkflowExecutableReferenceGarbageCollector garbageCollector,
        IOptions<WorkflowExecutableReferenceGarbageCollectionOptions> options,
        ILogger<WorkflowExecutableReferenceGarbageCollectionPumpTask> logger)
    {
        ArgumentNullException.ThrowIfNull(garbageCollector);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        _garbageCollector = garbageCollector;
        _options = options;
        _logger = logger;
    }

    /// <summary>The interval that will precede the next sweep: the baseline while healthy, widened geometrically under sustained failure.</summary>
    public TimeSpan CurrentSweepInterval => ComputeInterval();

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        try
        {
            var result = await _garbageCollector.SweepAsync(cancellationToken);
            _consecutiveSweepFailures = 0;

            if (result.DidWork && _logger.IsEnabled(LogLevel.Debug))
                _logger.LogDebug(
                    "Reference GC sweep removed {ReferenceCount} reference(s) and {ArtifactCount} artifact(s)",
                    result.DeletedReferenceCount,
                    result.DeletedArtifactCount);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not (OutOfMemoryException or StackOverflowException or AccessViolationException))
        {
            var failures = Interlocked.Increment(ref _consecutiveSweepFailures);
            _logger.LogError(
                exception,
                "Reference GC sweep failed ({ConsecutiveFailures} consecutive); backing off to {Interval}",
                failures,
                ComputeInterval());
        }
    }

    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public ITaskSchedule GetSchedule() => new AdaptiveIntervalSchedule(() => CurrentSweepInterval, _logger);

    private TimeSpan ComputeInterval()
    {
        var options = _options.Value;
        var failures = Volatile.Read(ref _consecutiveSweepFailures);
        return failures <= 0
            ? options.SweepInterval
            : ComputeBackoff(options.SweepInterval, options.MaxBackoffInterval, failures);
    }

    // Geometric backoff (base * 2^(failures-1)) clamped to maxInterval, guarded against overflow (mirrors the resumption pump).
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
