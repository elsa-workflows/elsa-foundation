using System.Diagnostics;
using Elsa.Diagnostics.Persistence.Draining;
using Elsa.Diagnostics.Persistence.Observability;
using Elsa.Diagnostics.Persistence.Tests.Fixtures;
using Xunit;

namespace Elsa.Diagnostics.Persistence.Tests;

public sealed class DiagnosticsDrainShutdownTests
{
    [Fact]
    public async Task Graceful_stop_drains_tail_applies_final_retention_and_completes_all_acks()
    {
        var counters = new DiagnosticsPersistenceCounters();
        var target = new DiagnosticsFailureTarget { RetentionDeleted = 3 };
        await using var drain = CreateDrain(target, counters, TimeSpan.FromSeconds(2));
        drain.Start();
        var acknowledgements = Enumerable.Range(1, 8).Select(x => drain.EnqueueAsync(x).AsTask()).ToArray();

        var result = await drain.StopAsync();

        Assert.True(result.Drained);
        Assert.Equal(DiagnosticsDrainState.Stopped, result.State);
        Assert.Equal(Enumerable.Range(1, 8), await Task.WhenAll(acknowledgements));
        Assert.True(target.RetentionCalls >= 1);
        Assert.Equal(3, counters.Snapshot().Losses[DiagnosticsPersistenceLossReason.DurableRetentionDeletion]);
    }

    [Fact]
    public async Task Timeout_is_bounded_and_completes_the_inflight_acknowledgement()
    {
        var target = new DiagnosticsFailureTarget
        {
            CommitRelease = new(TaskCreationOptions.RunContinuationsAsynchronously),
            IgnoreCancellation = true
        };
        var drain = CreateDrain(target, new DiagnosticsPersistenceCounters(), TimeSpan.FromMilliseconds(50));
        drain.Start();
        Assert.True(drain.TryEnqueue(1, out var acknowledgement));
        await target.CommitEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var stopwatch = Stopwatch.StartNew();

        var result = await drain.StopAsync();

        stopwatch.Stop();
        Assert.False(result.Drained);
        Assert.Equal(DiagnosticsDrainState.TimedOut, result.State);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(1));
        var exception = await Assert.ThrowsAsync<DiagnosticsDrainException>(() => acknowledgement);
        Assert.Equal(DiagnosticsPersistenceLossReason.ShutdownTimeout, exception.Reason);
        target.CommitRelease.SetResult();
        await drain.DisposeAsync();
    }

    [Fact]
    public async Task Timeout_and_overflow_leave_no_accepted_acknowledgement_incomplete()
    {
        var target = new DiagnosticsFailureTarget
        {
            CommitRelease = new(TaskCreationOptions.RunContinuationsAsynchronously),
            IgnoreCancellation = true
        };
        var drain = CreateDrain(target, new DiagnosticsPersistenceCounters(), TimeSpan.FromMilliseconds(50), queueCapacity: 2, batchSize: 1);
        drain.Start();
        var acknowledgements = new List<Task<int>>();
        Assert.True(drain.TryEnqueue(1, out var first));
        acknowledgements.Add(first);
        await target.CommitEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        foreach (var item in Enumerable.Range(2, 4))
        {
            Assert.True(drain.TryEnqueue(item, out var acknowledgement));
            acknowledgements.Add(acknowledgement);
        }

        await drain.StopAsync();

        Assert.All(acknowledgements, acknowledgement => Assert.True(acknowledgement.IsCompleted));
        target.CommitRelease.SetResult();
        await drain.DisposeAsync();
    }

    [Fact]
    public async Task Transient_final_retention_failure_retries_and_recovers()
    {
        var counters = new DiagnosticsPersistenceCounters();
        var target = new DiagnosticsFailureTarget
        {
            RetentionDeleted = 2,
            RetentionFailure = call => call < 3 ? new DiagnosticsOperationalException("retention unavailable") : null
        };
        await using var drain = CreateDrain(target, counters, TimeSpan.FromSeconds(2));
        var acknowledgement = drain.EnqueueAsync(1).AsTask();

        var result = await drain.StopAsync();

        Assert.True(result.Drained);
        Assert.Equal(1, await acknowledgement);
        Assert.Equal(3, target.RetentionCalls);
        Assert.Equal(2, counters.Snapshot().RetentionRetries);
        Assert.Equal(2, counters.Snapshot().Losses[DiagnosticsPersistenceLossReason.DurableRetentionDeletion]);
    }

    [Fact]
    public async Task Exhausted_final_retention_failure_is_classified_without_invalidating_commits()
    {
        var counters = new DiagnosticsPersistenceCounters();
        var target = new DiagnosticsFailureTarget
        {
            RetentionFailure = _ => new DiagnosticsOperationalException("retention unavailable")
        };
        await using var drain = CreateDrain(target, counters, TimeSpan.FromSeconds(2));
        var acknowledgement = drain.EnqueueAsync(1).AsTask();

        var result = await drain.StopAsync();

        Assert.True(result.Drained);
        Assert.Equal(1, await acknowledgement);
        Assert.Equal(3, target.RetentionCalls);
        var snapshot = counters.Snapshot();
        Assert.Equal(2, snapshot.RetentionRetries);
        Assert.Equal(1, snapshot.RetentionFailures);
    }

    private static DiagnosticsDrain<int, int> CreateDrain(
        DiagnosticsFailureTarget target,
        IDiagnosticsPersistenceObserver observer,
        TimeSpan shutdownTimeout,
        int queueCapacity = 16,
        int batchSize = 4) =>
        new(target, new()
        {
            BatchSize = batchSize,
            QueueCapacity = queueCapacity,
            RetentionInterval = 100,
            MaxAttempts = 3,
            BaseRetryDelay = TimeSpan.FromMilliseconds(1),
            MaxRetryDelay = TimeSpan.FromMilliseconds(2),
            ShutdownTimeout = shutdownTimeout
        }, observer);
}
