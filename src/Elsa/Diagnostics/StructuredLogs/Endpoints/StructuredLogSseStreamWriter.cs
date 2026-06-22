using Elsa.Diagnostics.StructuredLogs.Core.Models;
using Microsoft.AspNetCore.Http;

namespace Elsa.Diagnostics.StructuredLogs.Endpoints;

/// <summary>
/// Writes structured-log live feed items to an HTTP response as Server-Sent Events.
/// </summary>
public sealed class StructuredLogSseStreamWriter
{
    private static readonly TimeSpan DefaultHeartbeatInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan DefaultPendingMoveNextCleanupTimeout = TimeSpan.FromSeconds(5);
    private readonly StructuredLogSseFormatter _formatter;
    private readonly TimeSpan _heartbeatInterval;
    private readonly TimeSpan _pendingMoveNextCleanupTimeout;

    public StructuredLogSseStreamWriter(StructuredLogSseFormatter formatter) : this(formatter, DefaultHeartbeatInterval)
    {
    }

    public StructuredLogSseStreamWriter(StructuredLogSseFormatter formatter, TimeSpan heartbeatInterval) : this(formatter, heartbeatInterval, DefaultPendingMoveNextCleanupTimeout)
    {
    }

    public StructuredLogSseStreamWriter(StructuredLogSseFormatter formatter, TimeSpan heartbeatInterval, TimeSpan pendingMoveNextCleanupTimeout)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        if (heartbeatInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(heartbeatInterval), heartbeatInterval, "Heartbeat interval must be positive.");
        if (pendingMoveNextCleanupTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(pendingMoveNextCleanupTimeout), pendingMoveNextCleanupTimeout, "Pending MoveNext cleanup timeout must be positive.");

        _formatter = formatter;
        _heartbeatInterval = heartbeatInterval;
        _pendingMoveNextCleanupTimeout = pendingMoveNextCleanupTimeout;
    }

    public async Task StreamAsync(HttpResponse response, IAsyncEnumerable<StructuredLogStreamItem> stream, CancellationToken cancellationToken)
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
        IAsyncEnumerator<StructuredLogStreamItem> enumerator,
        Task<bool>? moveNext,
        CancellationTokenSource streamCts,
        TimeSpan pendingMoveNextCleanupTimeout)
    {
        if (moveNext is { IsCompleted: false })
        {
            await streamCts.CancelAsync();
            var completed = await Task.WhenAny(moveNext, Task.Delay(pendingMoveNextCleanupTimeout));
            if (completed != moveNext)
                // Disposing an async iterator while MoveNextAsync is still pending can throw NotSupportedException.
                return;

            try
            {
                await moveNext;
            }
            catch (OperationCanceledException) when (streamCts.IsCancellationRequested)
            {
            }
        }

        await enumerator.DisposeAsync();
    }
}
