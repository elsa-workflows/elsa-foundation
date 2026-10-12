using System.Runtime.CompilerServices;
using ConsoleLogStreaming.Core;
using ConsoleLogStreaming.Core.Models;

namespace Elsa.Studio.ConsoleStream;

/// <summary>
/// Decorates an <see cref="IConsoleLogProvider"/> so that live subscription items are released in
/// time-gated batches: once a batch has been released, the next batch is held back until
/// <paramref name="minReleaseInterval"/> has elapsed since the previous release.
///
/// <para>
/// This is a structural damper against the long-polling feedback storm (PR #215): when the SignalR
/// client falls back to long polling and the host emits a per-request stdout line (e.g. CShells'
/// <c>ShellMiddleware</c> "Resolved shell" log, <c>UseHttpLogging</c>, Serilog request logging),
/// every poll request produces a captured line whose push immediately completes the next pending
/// poll — a self-sustaining loop paced only by HTTP round-trip time. The gate caps that loop at one
/// release per interval regardless of what the host logs, so the defense no longer depends solely on
/// logging filters staying in place.
/// </para>
///
/// <para>
/// Items that arrive while the gate is closed are buffered by the inner provider's bounded
/// subscriber queue and drain as a single batch when the gate opens, so genuine bursts still stream
/// with at most <paramref name="minReleaseInterval"/> of added latency. A batch is defined as the
/// run of items synchronously available from the inner subscription; the gate only applies when the
/// buffer has been drained and a new item arrives asynchronously, so a sustained flood that keeps
/// the buffer non-empty is never throttled (it has no request feedback to dampen).
/// </para>
/// </summary>
public sealed class PacedConsoleLogProvider(
    IConsoleLogProvider inner,
    TimeSpan minReleaseInterval,
    TimeProvider? timeProvider = null) : IConsoleLogProvider, IConsoleLogDroppedLineReporter
{
    private readonly TimeProvider _timeProvider = timeProvider ?? TimeProvider.System;

    public ValueTask PublishAsync(ConsoleLogLine line, CancellationToken cancellationToken = default) =>
        inner.PublishAsync(line, cancellationToken);

    public ValueTask<RecentConsoleLogsResult> GetRecentAsync(ConsoleLogFilter filter, CancellationToken cancellationToken = default) =>
        inner.GetRecentAsync(filter, cancellationToken);

    public ValueTask<IReadOnlyCollection<ConsoleLogSource>> ListSourcesAsync(CancellationToken cancellationToken = default) =>
        inner.ListSourcesAsync(cancellationToken);

    public void ReportDropped(ConsoleLogDroppedSummary summary)
    {
        if (inner is IConsoleLogDroppedLineReporter reporter)
            reporter.ReportDropped(summary);
    }

    public async IAsyncEnumerable<ConsoleLogStreamingItem> SubscribeAsync(
        ConsoleLogFilter filter,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await using var source = inner.SubscribeAsync(filter, cancellationToken).GetAsyncEnumerator(cancellationToken);
        long? lastReleaseTimestamp = null;
        var draining = false;

        while (true)
        {
            var moveNext = source.MoveNextAsync();

            // An asynchronous wait means the inner subscription buffer is drained: the current
            // batch is complete and the next item opens a new batch behind the release gate.
            if (!moveNext.IsCompleted)
                draining = false;

            if (!await moveNext.ConfigureAwait(false))
                yield break;

            if (!draining)
            {
                if (lastReleaseTimestamp is { } previousRelease)
                {
                    var wait = minReleaseInterval - _timeProvider.GetElapsedTime(previousRelease);
                    if (wait > TimeSpan.Zero)
                        await Task.Delay(wait, _timeProvider, cancellationToken).ConfigureAwait(false);
                }

                lastReleaseTimestamp = _timeProvider.GetTimestamp();
                draining = true;
            }

            yield return source.Current;
        }
    }
}
