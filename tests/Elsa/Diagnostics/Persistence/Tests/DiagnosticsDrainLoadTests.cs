using Elsa.Diagnostics.Persistence.Draining;
using Elsa.Diagnostics.Persistence.Observability;
using Elsa.Diagnostics.Persistence.Tests.Fixtures;
using Xunit;

namespace Elsa.Diagnostics.Persistence.Tests;

public sealed class DiagnosticsDrainLoadTests
{
    [Fact]
    public async Task Concurrent_producers_complete_every_accepted_acknowledgement()
    {
        var target = new DiagnosticsFailureTarget();
        await using var drain = CreateDrain(target, queueCapacity: 512, batchSize: 25);
        drain.Start();

        var acknowledgements = Enumerable.Range(1, 200)
            .AsParallel()
            .Select(item =>
            {
                Assert.True(drain.TryEnqueue(item, out var acknowledgement));
                return acknowledgement;
            })
            .ToArray();

        await drain.StopAsync();
        var results = await Task.WhenAll(acknowledgements);
        Assert.Equal(Enumerable.Range(1, 200).Order(), results.Order());
        Assert.Equal(200, target.State.PersistedItems.Count);
    }

    [Fact]
    public async Task Overflow_sheds_the_oldest_item_and_settles_its_acknowledgement()
    {
        var counters = new DiagnosticsPersistenceCounters();
        await using var drain = CreateDrain(new(), queueCapacity: 2, observer: counters);
        Assert.True(drain.TryEnqueue(1, out var first));
        Assert.True(drain.TryEnqueue(2, out var second));
        Assert.True(drain.TryEnqueue(3, out var third));

        var exception = await Assert.ThrowsAsync<DiagnosticsDrainException>(() => first);
        Assert.Equal(DiagnosticsPersistenceLossReason.QueueOverflow, exception.Reason);
        drain.Start();
        await drain.StopAsync();
        Assert.Equal(new[] { 2, 3 }, await Task.WhenAll(second, third));
        Assert.Equal(1, counters.Snapshot().Losses[DiagnosticsPersistenceLossReason.QueueOverflow]);
    }

    [Fact]
    public async Task Retry_exhaustion_fails_one_batch_and_the_later_batch_recovers()
    {
        var counters = new DiagnosticsPersistenceCounters();
        var target = new DiagnosticsFailureTarget
        {
            Failure = (batch, _) => batch.Items[0] == 1 ? new DiagnosticsOperationalException("persistently unavailable") : null
        };
        await using var drain = CreateDrain(target, batchSize: 1, observer: counters);
        drain.Start();

        var failed = drain.EnqueueAsync(1).AsTask();
        var exception = await Assert.ThrowsAsync<DiagnosticsDrainException>(() => failed);
        Assert.Equal(DiagnosticsPersistenceLossReason.RetryExhausted, exception.Reason);
        var recovered = drain.EnqueueAsync(2).AsTask();

        await drain.StopAsync();
        Assert.Equal(2, await recovered);
        var snapshot = counters.Snapshot();
        Assert.Equal(2, snapshot.CommitRetries);
        Assert.Equal(1, snapshot.CommitFailures);
        Assert.Equal(1, snapshot.Losses[DiagnosticsPersistenceLossReason.RetryExhausted]);
    }

    private static DiagnosticsDrain<int, int> CreateDrain(
        DiagnosticsFailureTarget target,
        int queueCapacity = 16,
        int batchSize = 4,
        IDiagnosticsPersistenceObserver? observer = null) =>
        new(target, new()
        {
            BatchSize = batchSize,
            QueueCapacity = queueCapacity,
            RetentionInterval = 100,
            MaxAttempts = 3,
            BaseRetryDelay = TimeSpan.FromMilliseconds(1),
            MaxRetryDelay = TimeSpan.FromMilliseconds(2),
            ShutdownTimeout = TimeSpan.FromSeconds(2)
        }, observer);
}
