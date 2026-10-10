using Elsa.Diagnostics.Persistence.Draining;
using Elsa.Diagnostics.Persistence.Observability;
using Elsa.Diagnostics.Persistence.Tests.Fixtures;
using Xunit;

namespace Elsa.Diagnostics.Persistence.Tests;

public sealed class DiagnosticsDrainValidationTests : DiagnosticsDrainTestBase
{
    [Fact]
    public async Task Incomplete_commit_results_exhaust_retries_and_fail_every_acknowledgement()
    {
        var target = new ScriptedTarget
        {
            Commit = batch => new([batch.Items[0]], batch.Items.Count)
        };
        var drain = CreateDrain(target);
        var first = drain.EnqueueAsync(1).AsTask();
        var second = drain.EnqueueAsync(2).AsTask();

        await drain.StopAsync();

        Assert.Equal(DiagnosticsPersistenceLossReason.RetryExhausted,
            (await Assert.ThrowsAsync<DiagnosticsDrainException>(() => first)).Reason);
        Assert.Equal(DiagnosticsPersistenceLossReason.RetryExhausted,
            (await Assert.ThrowsAsync<DiagnosticsDrainException>(() => second)).Reason);
        Assert.Equal(2, target.CommitCalls);
    }

    [Fact]
    public async Task Null_commit_results_are_invalid_and_fail_with_retry_exhaustion()
    {
        var target = new ScriptedTarget { Commit = _ => new(null!, 0) };
        var drain = CreateDrain(target);
        var acknowledgement = drain.EnqueueAsync(1).AsTask();

        await drain.StopAsync();

        Assert.Equal(DiagnosticsPersistenceLossReason.RetryExhausted,
            (await Assert.ThrowsAsync<DiagnosticsDrainException>(() => acknowledgement)).Reason);
        Assert.Equal(2, target.CommitCalls);
    }

    [Fact]
    public async Task Negative_commit_retention_units_are_invalid_and_fail_with_retry_exhaustion()
    {
        var target = new ScriptedTarget { Commit = batch => new(batch.Items.ToArray(), -1) };
        var drain = CreateDrain(target);
        var acknowledgement = drain.EnqueueAsync(1).AsTask();

        await drain.StopAsync();

        Assert.Equal(DiagnosticsPersistenceLossReason.RetryExhausted,
            (await Assert.ThrowsAsync<DiagnosticsDrainException>(() => acknowledgement)).Reason);
        Assert.Equal(2, target.CommitCalls);
    }

    [Fact]
    public async Task Negative_retention_result_exhausts_retries_and_is_classified()
    {
        var counters = new DiagnosticsPersistenceCounters();
        var target = new ScriptedTarget { Retention = () => -1 };
        var drain = CreateDrain(target, counters);
        var acknowledgement = drain.EnqueueAsync(1).AsTask();

        var result = await drain.StopAsync();

        Assert.True(result.Drained);
        Assert.Equal(1, await acknowledgement);
        Assert.Equal(2, target.RetentionCalls);
        Assert.Equal(1, counters.Snapshot().RetentionRetries);
        Assert.Equal(1, counters.Snapshot().RetentionFailures);
    }

    private DiagnosticsDrain<int, int> CreateDrain(
        ScriptedTarget target,
        IDiagnosticsPersistenceObserver? observer = null) =>
        Fixture.Create(
            target,
            observer,
            maxAttempts: 2,
            baseRetryDelay: TimeSpan.Zero,
            maxRetryDelay: TimeSpan.Zero);

    [Fact]
    public async Task Pending_retention_barrier_applies_retention_below_the_periodic_interval()
    {
        // Cohort run 34088499054, PostgreSQL: every acknowledgement had completed but the last partial
        // interval's overflow was still retained when the workload inspected exact counts.
        var target = new ScriptedTarget { Retention = () => 7 };
        var drain = Fixture.Create(target, retentionInterval: 1_000, batchSize: 2);
        await drain.ApplyPendingRetentionAsync();
        Assert.Equal(0, target.RetentionCalls); // not started: nothing to apply

        drain.Start();
        var acknowledgements = Enumerable.Range(0, 5).Select(item => drain.EnqueueAsync(item).AsTask()).ToArray();
        await Task.WhenAll(acknowledgements);
        Assert.Equal(0, target.RetentionCalls); // five units, interval 1000: no periodic pass yet

        await drain.ApplyPendingRetentionAsync();
        Assert.Equal(1, target.RetentionCalls); // the barrier applies it now

        await drain.StopAsync();
        Assert.Equal(2, target.RetentionCalls); // the stop path's final pass is unchanged
        await drain.ApplyPendingRetentionAsync();
        Assert.Equal(2, target.RetentionCalls); // stopped: nothing to apply
    }

    [Fact]
    public async Task Retention_if_pending_skips_the_pass_the_loop_already_applied()
    {
        // #2533: the OpenTelemetry writer awaited its acknowledgement and then ran a second, identical retention
        // pass. With a periodic interval of one, the loop's pass already covers the commit.
        var target = new ScriptedTarget();
        var drain = Fixture.Create(target, retentionInterval: 1, batchSize: 1);
        drain.Start();

        await drain.EnqueueAsync(1);
        await drain.ApplyRetentionIfPendingAsync();

        Assert.Equal(1, target.RetentionCalls); // the loop's periodic pass only
        await drain.ApplyPendingRetentionAsync();
        Assert.Equal(2, target.RetentionCalls); // the unconditional barrier keeps its contract
    }

    [Fact]
    public async Task Retention_if_pending_applies_committed_units_below_the_periodic_interval()
    {
        var target = new ScriptedTarget();
        var drain = Fixture.Create(target, retentionInterval: 1_000, batchSize: 2);
        await drain.ApplyRetentionIfPendingAsync();
        Assert.Equal(0, target.RetentionCalls); // not started: nothing to apply

        drain.Start();
        await drain.ApplyRetentionIfPendingAsync();
        Assert.Equal(0, target.RetentionCalls); // running, nothing committed since start

        await Task.WhenAll(Enumerable.Range(0, 5).Select(item => drain.EnqueueAsync(item).AsTask()));
        await drain.ApplyRetentionIfPendingAsync();
        Assert.Equal(1, target.RetentionCalls); // five pending units: applied now
        await drain.ApplyRetentionIfPendingAsync();
        Assert.Equal(1, target.RetentionCalls); // already covered: skipped

        await drain.StopAsync();
        var afterStop = target.RetentionCalls;
        await drain.ApplyRetentionIfPendingAsync();
        Assert.Equal(afterStop, target.RetentionCalls); // stopped: nothing to apply
    }

    private sealed class ScriptedTarget : IDiagnosticsDrainTarget<int, int>
    {
        private int _commitCalls;
        private int _retentionCalls;

        public Func<DiagnosticsDrainBatch<int>, DiagnosticsDrainCommit<int>> Commit { get; init; } =
            batch => new(batch.Items.ToArray(), batch.Items.Count);

        public Func<int> Retention { get; init; } = () => 0;
        public int CommitCalls => Volatile.Read(ref _commitCalls);
        public int RetentionCalls => Volatile.Read(ref _retentionCalls);

        public ValueTask<DiagnosticsDrainCommit<int>> CommitAsync(
            DiagnosticsDrainBatch<int> batch,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _commitCalls);
            return ValueTask.FromResult(Commit(batch));
        }

        public ValueTask<int> ApplyRetentionAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _retentionCalls);
            return ValueTask.FromResult(Retention());
        }
    }
}
