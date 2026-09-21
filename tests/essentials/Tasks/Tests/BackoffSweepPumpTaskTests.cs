using Elsa.Tasks.Schedules;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Elsa.Tasks.Tests;

public sealed class BackoffSweepPumpTaskTests
{
    [Fact]
    public void Constructor_WhenLoggerIsNull_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new NullLoggerPump());
    }

    [Fact]
    public async Task ExecuteAsync_HandledFailure_WidensThenCleanSweepResets()
    {
        var pump = new TestPump { Fail = () => new InvalidOperationException("boom") };

        Assert.Equal(TimeSpan.FromSeconds(1), pump.CurrentSweepInterval);
        await pump.ExecuteAsync(CancellationToken.None);
        Assert.Equal(TimeSpan.FromSeconds(1), pump.CurrentSweepInterval);
        Assert.Equal(1, pump.FailuresReported);
        await pump.ExecuteAsync(CancellationToken.None);
        Assert.Equal(TimeSpan.FromSeconds(2), pump.CurrentSweepInterval);

        pump.Fail = null;
        await pump.ExecuteAsync(CancellationToken.None);
        Assert.Equal(TimeSpan.FromSeconds(1), pump.CurrentSweepInterval);
    }

    [Fact]
    public async Task ExecuteAsync_UnhandledException_Escapes()
    {
        var pump = new TestPump
        {
            Fail = () => new InvalidOperationException("fatal"),
            Handles = _ => false
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => pump.ExecuteAsync(CancellationToken.None));
        Assert.Equal(0, pump.FailuresReported);
    }

    [Fact]
    public async Task ExecuteAsync_Cancellation_RethrowsByDefault()
    {
        var pump = new TestPump { Fail = () => new OperationCanceledException() };

        await Assert.ThrowsAsync<OperationCanceledException>(() => pump.ExecuteAsync(CancellationToken.None));
        Assert.Equal(0, pump.FailuresReported);
    }

    [Fact]
    public async Task ExecuteAsync_ForeignCancellation_FeedsBackoffWhenNarrowed()
    {
        var pump = new TestPump
        {
            Fail = () => new OperationCanceledException(),
            RethrowCancellation = (_, token) => token.IsCancellationRequested
        };

        // The pump's own token is not cancelled, so the narrowed pump treats the foreign
        // cancellation as an ordinary sweep failure.
        await pump.ExecuteAsync(CancellationToken.None);

        Assert.Equal(1, pump.FailuresReported);
        Assert.Equal(TimeSpan.FromSeconds(1), pump.CurrentSweepInterval);
    }

    [Theory]
    [InlineData(0, 60, 1, 60)] // non-positive base falls back to the max
    [InlineData(120, 60, 1, 60)] // base at or above max clamps to the max
    [InlineData(1, 60, 3, 4)] // 1s * 2^(3-1)
    [InlineData(1, 60, 40, 60)] // large failure counts clamp instead of overflowing
    public void ComputeBackoff_CoversClampBranches(int baseSeconds, int maxSeconds, int failures, int expectedSeconds)
    {
        var actual = TestPump.Backoff(
            TimeSpan.FromSeconds(baseSeconds),
            TimeSpan.FromSeconds(maxSeconds),
            failures);

        Assert.Equal(TimeSpan.FromSeconds(expectedSeconds), actual);
    }

    private sealed class NullLoggerPump() : BackoffSweepPumpTask(null!)
    {
        protected override TimeSpan SweepInterval => TimeSpan.FromSeconds(1);
        protected override TimeSpan MaxBackoffInterval => TimeSpan.FromSeconds(60);
        protected override Task SweepAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        protected override void OnSweepFailed(Exception exception, int consecutiveFailures, TimeSpan backoffInterval)
        {
        }
    }

    private sealed class TestPump() : BackoffSweepPumpTask(NullLogger.Instance)
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
