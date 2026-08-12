using Microsoft.AspNetCore.Http;

namespace Elsa.Api.FastEndpoints.Sse;

/// <summary>
/// Formats one live-feed item into its Server-Sent Events frame. The heartbeat frame keeps the
/// connection alive while the stream is idle.
/// </summary>
public interface ISseStreamFormatter<in TItem>
{
    string Format(TItem item);

    string Heartbeat();
}

/// <summary>
/// Writes a live feed to an HTTP response as Server-Sent Events: items as formatted frames, heartbeats
/// while the stream is idle, and bounded cleanup of a pending <c>MoveNextAsync</c> on disconnect.
/// </summary>
public sealed class SseStreamWriter<TItem>
{
    private static readonly TimeSpan DefaultHeartbeatInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan DefaultPendingMoveNextCleanupTimeout = TimeSpan.FromSeconds(5);
    private readonly ISseStreamFormatter<TItem> _formatter;
    private readonly TimeSpan _heartbeatInterval;
    private readonly TimeSpan _pendingMoveNextCleanupTimeout;

    public SseStreamWriter(
        ISseStreamFormatter<TItem> formatter,
        TimeSpan? heartbeatInterval = null,
        TimeSpan? pendingMoveNextCleanupTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        if (heartbeatInterval is { } heartbeat && heartbeat <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(heartbeatInterval), heartbeat, "Heartbeat interval must be positive.");
        if (pendingMoveNextCleanupTimeout is { } cleanup && cleanup <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(pendingMoveNextCleanupTimeout), cleanup, "Pending MoveNext cleanup timeout must be positive.");

        _formatter = formatter;
        _heartbeatInterval = heartbeatInterval ?? DefaultHeartbeatInterval;
        _pendingMoveNextCleanupTimeout = pendingMoveNextCleanupTimeout ?? DefaultPendingMoveNextCleanupTimeout;
    }

    public async Task StreamAsync(HttpResponse response, IAsyncEnumerable<TItem> stream, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(response);
        ArgumentNullException.ThrowIfNull(stream);

        using var streamCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var enumerator = stream.GetAsyncEnumerator(streamCts.Token);
        Task<bool>? moveNext = null;

        try
        {
            moveNext = enumerator.MoveNextAsync().AsTask();
            while (true)
            {
                using var heartbeatCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var completed = await Task.WhenAny(moveNext, Task.Delay(_heartbeatInterval, heartbeatCts.Token));

                if (completed != moveNext)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await response.WriteAsync(_formatter.Heartbeat(), cancellationToken);
                    await response.Body.FlushAsync(cancellationToken);
                    continue;
                }

                await heartbeatCts.CancelAsync();

                if (!await moveNext)
                    break;

                await response.WriteAsync(_formatter.Format(enumerator.Current), cancellationToken);
                await response.Body.FlushAsync(cancellationToken);
                moveNext = enumerator.MoveNextAsync().AsTask();
            }
        }
        finally
        {
            await DisposeAfterPendingMoveNextAsync(enumerator, moveNext, streamCts, _pendingMoveNextCleanupTimeout);
        }
    }

    private static async Task DisposeAfterPendingMoveNextAsync(
        IAsyncEnumerator<TItem> enumerator,
        Task<bool>? moveNext,
        CancellationTokenSource streamCts,
        TimeSpan pendingMoveNextCleanupTimeout)
    {
        if (moveNext is { IsCompleted: false })
        {
            await streamCts.CancelAsync();
            var completed = await Task.WhenAny(moveNext, Task.Delay(pendingMoveNextCleanupTimeout));
            if (completed == moveNext)
            {
                try
                {
                    await moveNext;
                }
                catch (OperationCanceledException) when (streamCts.IsCancellationRequested)
                {
                }
            }
        }

        var moveNextStillPending = moveNext is { IsCompleted: false };
        try
        {
            await enumerator.DisposeAsync();
        }
        catch (NotSupportedException) when (moveNextStillPending)
        {
            // Some async iterators reject DisposeAsync while MoveNextAsync is still pending.
        }
    }
}
