using System.Threading.Channels;
using ConsoleLogStreaming.Core;
using ConsoleLogStreaming.Core.Models;
using Elsa.Studio.ConsoleStream;
using Microsoft.Extensions.Time.Testing;

namespace Elsa.Studio.Tests;

/// <summary>
/// Regression tests for the long-polling feedback storm (PR #215): one captured stdout line must not
/// be able to complete one pending long poll 1:1. <see cref="PacedConsoleLogProvider"/> releases live
/// items in batches at most once per release interval, so the tests assert that a follow-up line is
/// held until the (fake) clock advances, while buffered lines still drain together.
/// </summary>
public sealed class PacedConsoleLogProviderTests : IAsyncDisposable
{
    private static readonly TimeSpan ReleaseInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan RealTimeout = TimeSpan.FromSeconds(10);

    // Long enough for the pacer to observably swallow a line that a broken gate would deliver
    // instantly, short enough to keep the suite fast. The gate itself runs on fake time, so this
    // real-time wait can never open it.
    private static readonly TimeSpan HeldAssertionWindow = TimeSpan.FromMilliseconds(250);

    private readonly FakeTimeProvider _time = new();
    private readonly ChannelConsoleLogProvider _inner = new();
    private readonly IAsyncEnumerator<ConsoleLogStreamingItem> _subscription;

    public PacedConsoleLogProviderTests()
    {
        var paced = new PacedConsoleLogProvider(_inner, ReleaseInterval, _time);
        _subscription = paced.SubscribeAsync(new ConsoleLogFilter()).GetAsyncEnumerator();
    }

    public async ValueTask DisposeAsync() => await _subscription.DisposeAsync();

    [Fact]
    public async Task ReleasesTheFirstLineWithoutDelay()
    {
        await _inner.PublishAsync(Line("first"));

        Assert.True(await _subscription.MoveNextAsync().AsTask().WaitAsync(RealTimeout));
        Assert.Equal("first", _subscription.Current.Line!.Text);
    }

    [Fact]
    public async Task HoldsAFollowUpLineUntilTheReleaseIntervalElapses()
    {
        await ReceiveFirstLineAsync();

        var next = _subscription.MoveNextAsync().AsTask();
        await _inner.PublishAsync(Line("second"));
        await Task.Delay(HeldAssertionWindow);

        // The 1:1 storm shape: without the gate, "second" would have completed this pull instantly.
        Assert.False(next.IsCompleted);

        _time.Advance(ReleaseInterval);
        Assert.True(await WaitDrivingFakeTimeAsync(next));
        Assert.Equal("second", _subscription.Current.Line!.Text);
    }

    [Fact]
    public async Task ReleasesLinesBufferedBehindTheGateAsOneBatch()
    {
        await ReceiveFirstLineAsync();

        var next = _subscription.MoveNextAsync().AsTask();
        await _inner.PublishAsync(Line("second"));
        await _inner.PublishAsync(Line("third"));
        await _inner.PublishAsync(Line("fourth"));
        await Task.Delay(HeldAssertionWindow);
        Assert.False(next.IsCompleted);

        _time.Advance(ReleaseInterval);
        Assert.True(await WaitDrivingFakeTimeAsync(next));
        Assert.Equal("second", _subscription.Current.Line!.Text);

        // The rest of the batch drains on real time alone: no further clock advancement.
        Assert.True(await _subscription.MoveNextAsync().AsTask().WaitAsync(RealTimeout));
        Assert.Equal("third", _subscription.Current.Line!.Text);
        Assert.True(await _subscription.MoveNextAsync().AsTask().WaitAsync(RealTimeout));
        Assert.Equal("fourth", _subscription.Current.Line!.Text);
    }

    [Fact]
    public async Task StreamsLinesAlreadyBufferedAtSubscribeAsOneImmediateBatch()
    {
        await _inner.PublishAsync(Line("first"));
        await _inner.PublishAsync(Line("second"));

        Assert.True(await _subscription.MoveNextAsync().AsTask().WaitAsync(RealTimeout));
        Assert.Equal("first", _subscription.Current.Line!.Text);
        Assert.True(await _subscription.MoveNextAsync().AsTask().WaitAsync(RealTimeout));
        Assert.Equal("second", _subscription.Current.Line!.Text);
    }

    [Fact]
    public void ForwardsDroppedLineReportsToTheInnerProvider()
    {
        var paced = new PacedConsoleLogProvider(_inner, ReleaseInterval, _time);
        var summary = new ConsoleLogDroppedSummary { Reason = "capture-overflow", Count = 3 };

        ((IConsoleLogDroppedLineReporter)paced).ReportDropped(summary);

        Assert.Same(summary, Assert.Single(_inner.DroppedReports));
    }

    private async Task ReceiveFirstLineAsync()
    {
        await _inner.PublishAsync(Line("first"));
        Assert.True(await _subscription.MoveNextAsync().AsTask().WaitAsync(RealTimeout));
    }

    /// <summary>
    /// Awaits the pending pull while nudging the fake clock, so the assertion cannot race the
    /// pacer registering its gate timer.
    /// </summary>
    private async Task<bool> WaitDrivingFakeTimeAsync(Task<bool> pendingMoveNext)
    {
        var deadline = DateTime.UtcNow + RealTimeout;
        while (!pendingMoveNext.IsCompleted && DateTime.UtcNow < deadline)
        {
            _time.Advance(TimeSpan.FromMilliseconds(20));
            await Task.WhenAny(pendingMoveNext, Task.Delay(TimeSpan.FromMilliseconds(10)));
        }

        return await pendingMoveNext.WaitAsync(TimeSpan.FromSeconds(1));
    }

    private static ConsoleLogLine Line(string text) => new() { Text = text };

    private sealed class ChannelConsoleLogProvider : IConsoleLogProvider, IConsoleLogDroppedLineReporter
    {
        private readonly Channel<ConsoleLogStreamingItem> _items = Channel.CreateUnbounded<ConsoleLogStreamingItem>();

        public List<ConsoleLogDroppedSummary> DroppedReports { get; } = [];

        public ValueTask PublishAsync(ConsoleLogLine line, CancellationToken cancellationToken = default)
        {
            _items.Writer.TryWrite(ConsoleLogStreamingItem.FromLine(line));
            return ValueTask.CompletedTask;
        }

        public IAsyncEnumerable<ConsoleLogStreamingItem> SubscribeAsync(ConsoleLogFilter filter, CancellationToken cancellationToken = default) =>
            _items.Reader.ReadAllAsync(cancellationToken);

        public void ReportDropped(ConsoleLogDroppedSummary summary) => DroppedReports.Add(summary);

        public ValueTask<RecentConsoleLogsResult> GetRecentAsync(ConsoleLogFilter filter, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<IReadOnlyCollection<ConsoleLogSource>> ListSourcesAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
