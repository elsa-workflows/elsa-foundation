using System.Collections.Concurrent;
using Elsa.Diagnostics.Persistence.Draining;
using Elsa.Diagnostics.Persistence.Observability;
using Elsa.Diagnostics.Persistence.Tests.Fixtures;
using Xunit;

namespace Elsa.Diagnostics.Persistence.Tests;

public sealed class DiagnosticsDrainLoadTests : DiagnosticsDrainTestBase
{
    [Fact]
    public async Task Concurrent_producers_complete_every_accepted_acknowledgement()
    {
        var target = new DiagnosticsFailureTarget();
        var drain = Fixture.Create(target, queueCapacity: 512, batchSize: 25);
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
        var drain = Fixture.Create(new DiagnosticsFailureTarget(), counters, queueCapacity: 2);
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
    public async Task Counters_expose_accepted_queue_depth_and_high_water_without_capture_data()
    {
        var counters = new DiagnosticsPersistenceCounters();
        var drain = Fixture.Create(new DiagnosticsFailureTarget(), counters, queueCapacity: 2);

        Assert.True(drain.TryEnqueue(1, out var first));
        Assert.True(drain.TryEnqueue(2, out var second));
        var queued = counters.Snapshot();
        Assert.Equal(2, queued.Accepted);
        Assert.Equal(2, queued.QueueDepth);
        Assert.Equal(2, queued.QueueHighWater);

        drain.Start();
        await drain.StopAsync();
        await Task.WhenAll(first, second);

        var drained = counters.Snapshot();
        Assert.Equal(2, drained.Accepted);
        Assert.Equal(0, drained.QueueDepth);
        Assert.Equal(2, drained.QueueHighWater);
    }

    [Fact]
    public async Task Concurrent_producers_preserve_ordered_final_queue_depth_and_exact_high_water()
    {
        const int queueCapacity = 32;
        var counters = new DiagnosticsPersistenceCounters();
        var drain = Fixture.Create(new DiagnosticsFailureTarget(), counters, queueCapacity: queueCapacity, batchSize: 8);
        var acknowledgements = new ConcurrentBag<Task<int>>();

        await Task.WhenAll(Enumerable.Range(1, 1_000).Select(item => Task.Run(() =>
        {
            Assert.True(drain.TryEnqueue(item, out var acknowledgement));
            acknowledgements.Add(acknowledgement);
        })));

        drain.Start();
        await drain.StopAsync();
        foreach (var acknowledgement in acknowledgements)
        {
            try
            {
                await acknowledgement;
            }
            catch (DiagnosticsDrainException exception) when (exception.Reason == DiagnosticsPersistenceLossReason.QueueOverflow)
            {
                // Expected for captures explicitly shed by the bounded queue.
            }
        }

        var snapshot = counters.Snapshot();
        Assert.Equal(0, snapshot.QueueDepth);
        Assert.Equal(queueCapacity, snapshot.QueueHighWater);
    }

    [Fact]
    public async Task Retry_exhaustion_fails_one_batch_and_the_later_batch_recovers()
    {
        var counters = new DiagnosticsPersistenceCounters();
        var target = new DiagnosticsFailureTarget
        {
            Failure = (batch, _) => batch.Items[0] == 1 ? new DiagnosticsOperationalException("persistently unavailable") : null
        };
        var drain = Fixture.Create(target, counters, batchSize: 1);
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

}
