using System.Reflection;
using Elsa.Tasks.Schedules;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Elsa.Tasks.Tests;

public sealed class BackoffSweepWorkerTaskTests
{
    [Fact]
    public void Constructor_WhenLoggerIsNull_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new NullLoggerWorker());
    }

    /// <summary>
    /// Every derived worker gets its schedule from the base: an adaptive interval schedule whose next wait is the worker's
    /// current sweep interval, so failed sweeps widen the wait before the next one.
    /// </summary>
    [Fact]
    public async Task GetSchedule_WaitsTheWidenedSweepIntervalAfterFailures()
    {
        var worker = new TestWorker { Fail = () => new InvalidOperationException("boom") };
        var schedule = Assert.IsType<AdaptiveIntervalSchedule>(worker.GetSchedule());
        await using var execution = Assert.IsType<ScheduledTaskExecution>(schedule.ScheduleExecution(() => Task.CompletedTask));
        // The interval the execution re-arms its timer with after every run.
        var nextWait = (Func<TimeSpan>)typeof(ScheduledTaskExecution)
            .GetField("_interval", BindingFlags.NonPublic | BindingFlags.Instance)!
            .GetValue(execution)!;
        Assert.Equal(TimeSpan.FromSeconds(1), nextWait());

        await worker.ExecuteAsync(CancellationToken.None);
        await worker.ExecuteAsync(CancellationToken.None);

        Assert.Equal(TimeSpan.FromSeconds(2), nextWait());
    }

    [Fact]
    public async Task ExecuteAsync_HandledFailure_WidensThenCleanSweepResets()
    {
        var worker = new TestWorker { Fail = () => new InvalidOperationException("boom") };

        Assert.Equal(TimeSpan.FromSeconds(1), worker.CurrentSweepInterval);
        await worker.ExecuteAsync(CancellationToken.None);
        Assert.Equal(TimeSpan.FromSeconds(1), worker.CurrentSweepInterval);
        Assert.Equal(1, worker.FailuresReported);
        await worker.ExecuteAsync(CancellationToken.None);
        Assert.Equal(TimeSpan.FromSeconds(2), worker.CurrentSweepInterval);

        worker.Fail = null;
        await worker.ExecuteAsync(CancellationToken.None);
        Assert.Equal(TimeSpan.FromSeconds(1), worker.CurrentSweepInterval);
    }

    [Fact]
    public async Task ExecuteAsync_UnhandledException_Escapes()
    {
        var worker = new TestWorker
        {
            Fail = () => new InvalidOperationException("fatal"),
            Handles = _ => false
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => worker.ExecuteAsync(CancellationToken.None));
        Assert.Equal(0, worker.FailuresReported);
    }

    [Fact]
    public async Task ExecuteAsync_Cancellation_RethrowsByDefault()
    {
        var worker = new TestWorker { Fail = () => new OperationCanceledException() };

        await Assert.ThrowsAsync<OperationCanceledException>(() => worker.ExecuteAsync(CancellationToken.None));
        Assert.Equal(0, worker.FailuresReported);
    }

    [Fact]
    public async Task ExecuteAsync_ForeignCancellation_FeedsBackoffWhenNarrowed()
    {
        var worker = new TestWorker
        {
            Fail = () => new OperationCanceledException(),
            RethrowCancellation = (_, token) => token.IsCancellationRequested
        };

        // The worker's own token is not cancelled, so the narrowed worker treats the foreign
        // cancellation as an ordinary sweep failure.
        await worker.ExecuteAsync(CancellationToken.None);

        Assert.Equal(1, worker.FailuresReported);
        Assert.Equal(TimeSpan.FromSeconds(1), worker.CurrentSweepInterval);
    }

    [Theory]
    [InlineData(0, 60, 1, 60)] // non-positive base falls back to the max
    [InlineData(120, 60, 1, 60)] // base at or above max clamps to the max
    [InlineData(1, 60, 3, 4)] // 1s * 2^(3-1)
    [InlineData(1, 60, 40, 60)] // large failure counts clamp instead of overflowing
    public void ComputeBackoff_CoversClampBranches(int baseSeconds, int maxSeconds, int failures, int expectedSeconds)
    {
        var actual = TestWorker.Backoff(
            TimeSpan.FromSeconds(baseSeconds),
            TimeSpan.FromSeconds(maxSeconds),
            failures);

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), actual);
    }

    private sealed class NullLoggerWorker() : BackoffSweepWorkerTask(null!)
    {
        protected override TimeSpan SweepInterval => TimeSpan.FromSeconds(1);
        protected override TimeSpan MaxBackoffInterval => TimeSpan.FromSeconds(60);
        protected override Task SweepAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        protected override void OnSweepFailed(Exception exception, int consecutiveFailures, TimeSpan backoffInterval)
        {
        }
    }

    private sealed class TestWorker() : BackoffSweepWorkerTask(NullLogger.Instance)
    {
        public Func<Exception>? Fail { get; set; }
        public Func<Exception, bool>? Handles { get; init; }
        public Func<OperationCanceledException, CancellationToken, bool>? RethrowCancellation { get; init; }
        public int FailuresReported { get; private set; }

        protected override TimeSpan SweepInterval => TimeSpan.FromSeconds(1);

        protected override TimeSpan MaxBackoffInterval => TimeSpan.FromSeconds(60);

        protected override Task SweepAsync(CancellationToken cancellationToken) =>
            Fail is { } fail ? Task.FromException(fail()) : Task.CompletedTask;

        protected override void OnSweepFailed(Exception exception, int consecutiveFailures, TimeSpan backoffInterval) =>
            FailuresReported = consecutiveFailures;

        protected override bool IsHandledSweepException(Exception exception) =>
            Handles?.Invoke(exception) ?? base.IsHandledSweepException(exception);

        protected override bool ShouldRethrowCancellation(OperationCanceledException exception, CancellationToken cancellationToken) =>
            RethrowCancellation?.Invoke(exception, cancellationToken) ?? base.ShouldRethrowCancellation(exception, cancellationToken);

        public static TimeSpan Backoff(TimeSpan baseInterval, TimeSpan maxInterval, int failures) =>
            ComputeBackoff(baseInterval, maxInterval, failures);
    }
}
