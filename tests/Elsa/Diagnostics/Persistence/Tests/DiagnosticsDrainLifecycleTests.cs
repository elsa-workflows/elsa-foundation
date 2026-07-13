using Elsa.Diagnostics.Persistence.Draining;
using Elsa.Diagnostics.Persistence.Observability;
using Elsa.Diagnostics.Persistence.Tests.Fixtures;
using Xunit;

namespace Elsa.Diagnostics.Persistence.Tests;

public sealed class DiagnosticsDrainLifecycleTests
{
    [Fact]
    public async Task Accepted_item_completes_only_after_authoritative_commit()
    {
        var target = new DiagnosticsFailureTarget { CommitRelease = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        await using var drain = CreateDrain(target);
        drain.Start();

        Assert.True(drain.TryEnqueue(42, out var acknowledgement));
        await target.CommitEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.False(acknowledgement.IsCompleted);

        target.CommitRelease.SetResult();
        Assert.Equal(42, await acknowledgement);
        Assert.Equal(DiagnosticsDrainState.Stopped, (await drain.StopAsync()).State);
    }

    [Fact]
    public async Task Acknowledgement_loss_retries_the_same_batch_identity_and_returns_original_result()
    {
        var target = new DiagnosticsFailureTarget { LoseFirstAcknowledgement = true };
        await using var drain = CreateDrain(target);
        drain.Start();
        var acknowledgement = drain.EnqueueAsync(7).AsTask();

        await drain.StopAsync();

        Assert.Equal(7, await acknowledgement);
        Assert.Equal(2, target.Attempts);
        Assert.Single(target.AttemptedBatchIds.Distinct());
        Assert.Single(target.State.PersistedItems);
    }

    [Fact]
    public async Task Caller_cancellation_does_not_abandon_an_accepted_acknowledgement()
    {
        var target = new DiagnosticsFailureTarget { CommitRelease = new(TaskCreationOptions.RunContinuationsAsynchronously) };
        await using var drain = CreateDrain(target);
        drain.Start();
        using var cancellation = new CancellationTokenSource();
        var callerWait = drain.EnqueueAsync(9, cancellation.Token).AsTask();
        await target.CommitEntered.Task.WaitAsync(TimeSpan.FromSeconds(2));

        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => callerWait);

        target.CommitRelease.SetResult();
        await drain.StopAsync();
        Assert.Equal(new[] { 9 }, target.State.PersistedItems);
    }

    [Fact]
    public async Task Writes_after_closure_are_rejected_with_an_explicit_loss_reason()
    {
        var counters = new DiagnosticsPersistenceCounters();
        await using var drain = CreateDrain(new(), counters);
        drain.Start();
        await drain.StopAsync();

        Assert.False(drain.TryEnqueue(1, out var acknowledgement));
        var exception = await Assert.ThrowsAsync<DiagnosticsDrainException>(() => acknowledgement);
        Assert.Equal(DiagnosticsPersistenceLossReason.WriteAfterClosure, exception.Reason);
        Assert.Equal(1, counters.Snapshot().Losses[DiagnosticsPersistenceLossReason.WriteAfterClosure]);
    }

    [Fact]
    public async Task Async_disposal_gracefully_drains_and_is_idempotent()
    {
        var target = new DiagnosticsFailureTarget();
        var drain = CreateDrain(target);
        var acknowledgement = drain.EnqueueAsync(11).AsTask();

        await drain.DisposeAsync();
        await drain.DisposeAsync();

        Assert.Equal(11, await acknowledgement);
        Assert.Equal(DiagnosticsDrainState.Stopped, drain.State);
    }

    [Fact]
    public async Task Synchronous_disposal_settles_queued_acknowledgements_and_is_idempotent()
    {
        var counters = new DiagnosticsPersistenceCounters();
        var drain = CreateDrain(new(), counters);
        Assert.True(drain.TryEnqueue(12, out var acknowledgement));

        drain.Dispose();
        drain.Dispose();

        var exception = await Assert.ThrowsAsync<DiagnosticsDrainException>(() => acknowledgement);
        Assert.Equal(DiagnosticsPersistenceLossReason.ShutdownTimeout, exception.Reason);
        Assert.Equal(DiagnosticsDrainState.TimedOut, drain.State);
        Assert.Equal(1, counters.Snapshot().Losses[DiagnosticsPersistenceLossReason.ShutdownTimeout]);
    }

    private static DiagnosticsDrain<int, int> CreateDrain(
        DiagnosticsFailureTarget target,
        IDiagnosticsPersistenceObserver? observer = null) =>
        new(target, Options(), observer);

    private static DiagnosticsDrainOptions Options() => new()
    {
        BatchSize = 4,
        QueueCapacity = 16,
        RetentionInterval = 100,
        MaxAttempts = 3,
        BaseRetryDelay = TimeSpan.FromMilliseconds(1),
        MaxRetryDelay = TimeSpan.FromMilliseconds(4),
        ShutdownTimeout = TimeSpan.FromSeconds(2)
    };
}
