using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// Locks the fan-in notifier's run-path failure policy (spec 089 D T005): every observer is invoked, and a throw
/// is caught and logged rather than propagated, so one bad observer neither faults the run nor starves the others.
/// </summary>
public sealed class BookmarkLifecycleNotifierTests
{
    private static readonly BookmarkState Bookmark = new(
        BookmarkId: "bk-1",
        WorkflowExecutionId: "wfexec-1",
        ActivityExecutionId: "ae-1",
        ExecutableNodeId: "node-1",
        ResumeTargetId: "resume-1",
        StimulusType: "HttpEndpoint",
        StimulusHash: "h1",
        Payload: null,
        Metadata: new Dictionary<string, string>(),
        CreatedAt: DateTimeOffset.UnixEpoch,
        ExpiresAt: null);

    [Fact]
    public async Task NotifyCreatedAndConsumed_InvokesEveryObserver()
    {
        var observer = new RecordingBookmarkLifecycleObserver();
        var notifier = new BookmarkLifecycleNotifier([observer]);

        await notifier.NotifyCreatedAsync(Bookmark);
        await notifier.NotifyConsumedAsync(Bookmark);

        Assert.Equal("bk-1", Assert.Single(observer.Created).BookmarkId);
        Assert.Equal("bk-1", Assert.Single(observer.Consumed).BookmarkId);
    }

    [Fact]
    public async Task ThrowingObserver_IsSwallowed_AndLaterObserversStillRun()
    {
        var recording = new RecordingBookmarkLifecycleObserver();
        var notifier = new BookmarkLifecycleNotifier([new ThrowingBookmarkLifecycleObserver(), recording]);

        // No throw escapes even though the first observer throws, and the second still runs.
        await notifier.NotifyCreatedAsync(Bookmark);

        Assert.Single(recording.Created);
    }

    [Fact]
    public async Task NeverCompletingObserver_TimesOut_AndLaterObserversReceiveCreatedAndConsumed()
    {
        var recording = new RecordingBookmarkLifecycleObserver();
        var notifier = new BookmarkLifecycleNotifier(
            [new NeverCompletingBookmarkLifecycleObserver(), recording],
            observerTimeout: TimeSpan.FromMilliseconds(25));

        await notifier.NotifyCreatedAsync(Bookmark);
        await notifier.NotifyConsumedAsync(Bookmark);

        Assert.Equal("bk-1", Assert.Single(recording.Created).BookmarkId);
        Assert.Equal("bk-1", Assert.Single(recording.Consumed).BookmarkId);
    }

    [Fact]
    public async Task CooperativeObserver_ReceivesTimeoutCancellation_AndLaterObserverStillRuns()
    {
        var cooperative = new CooperativeTimeoutBookmarkLifecycleObserver();
        var recording = new RecordingBookmarkLifecycleObserver();
        var notifier = new BookmarkLifecycleNotifier(
            [cooperative, recording],
            observerTimeout: TimeSpan.FromMilliseconds(25));

        await notifier.NotifyCreatedAsync(Bookmark);

        // The notifier abandons a timed-out observer instead of awaiting it, so its cancellation handling races
        // the assertions below. NotifyCreatedAsync returning proves the timeout fired, which guarantees this
        // signal completes; awaiting it is what makes the assertion deterministic under load.
        await cooperative.CancellationHandled;

        Assert.True(cooperative.CancellationObserved);
        Assert.Equal("bk-1", Assert.Single(recording.Created).BookmarkId);
    }

    [Fact]
    public async Task NoObservers_IsANoOp()
    {
        var notifier = new BookmarkLifecycleNotifier();

        await notifier.NotifyCreatedAsync(Bookmark);
        await notifier.NotifyConsumedAsync(Bookmark);
    }

    private sealed class NeverCompletingBookmarkLifecycleObserver : IBookmarkLifecycleObserver
    {
        private readonly TaskCompletionSource _neverCompletes = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask OnBookmarkCreatedAsync(BookmarkState bookmark, CancellationToken cancellationToken = default) =>
            new(_neverCompletes.Task);

        public ValueTask OnBookmarkConsumedAsync(BookmarkState bookmark, CancellationToken cancellationToken = default) =>
            new(_neverCompletes.Task);
    }

    private sealed class CooperativeTimeoutBookmarkLifecycleObserver : IBookmarkLifecycleObserver
    {
        private readonly TaskCompletionSource _cancellationHandled = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool CancellationObserved { get; private set; }

        /// <summary>Completes once the observer has reacted to cancellation and recorded <see cref="CancellationObserved"/>.</summary>
        public Task CancellationHandled => _cancellationHandled.Task;

        public ValueTask OnBookmarkCreatedAsync(BookmarkState bookmark, CancellationToken cancellationToken = default) =>
            WaitForCancellationAsync(cancellationToken);

        public ValueTask OnBookmarkConsumedAsync(BookmarkState bookmark, CancellationToken cancellationToken = default) =>
            ValueTask.CompletedTask;

        private async ValueTask WaitForCancellationAsync(CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            }
            finally
            {
                CancellationObserved = cancellationToken.IsCancellationRequested;
                _cancellationHandled.TrySetResult();
            }
        }
    }
}
