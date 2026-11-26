namespace Elsa.Diagnostics.Persistence.Draining;

/// <summary>Bounds one diagnostics capture drain's queue, batches, retries, retention, and shutdown.</summary>
public sealed class DiagnosticsDrainOptions
{
    public int BatchSize { get; init; } = 100;
    public int QueueCapacity { get; init; } = 1_000;
    public int RetentionInterval { get; init; } = 5_000;
    public int MaxAttempts { get; init; } = 9;
    public TimeSpan BaseRetryDelay { get; init; } = TimeSpan.FromMilliseconds(50);
    public TimeSpan MaxRetryDelay { get; init; } = TimeSpan.FromSeconds(5);
    public TimeSpan ShutdownTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// How long the drain lets a partial batch gather before committing it. Zero (the default) commits whatever is queued
    /// the moment the drain wakes, which under a slow trickle of captures means one commit per capture. A short delay
    /// trades that much commit latency for fewer, larger commits; a full batch never waits (#2613).
    /// </summary>
    public TimeSpan FlushDelay { get; init; } = TimeSpan.Zero;

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(BatchSize, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(QueueCapacity, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(RetentionInterval, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxAttempts, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(BaseRetryDelay, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxRetryDelay, BaseRetryDelay);
        ArgumentOutOfRangeException.ThrowIfLessThan(ShutdownTimeout, TimeSpan.Zero);
        ArgumentOutOfRangeException.ThrowIfLessThan(FlushDelay, TimeSpan.Zero);
    }
}
