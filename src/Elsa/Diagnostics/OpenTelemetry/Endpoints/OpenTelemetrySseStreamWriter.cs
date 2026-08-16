using System.Text;
using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Microsoft.AspNetCore.Http;

namespace Elsa.Diagnostics.OpenTelemetry.Endpoints;

/// <summary>Owner-local SSE writer; keeping this seam here avoids a FastEndpoints runtime dependency.</summary>
public sealed class OpenTelemetrySseStreamWriter(OpenTelemetrySseFormatter formatter)
{
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan PendingMoveNextCleanupTimeout = TimeSpan.FromSeconds(5);

    public async Task StreamAsync(HttpResponse response, IAsyncEnumerable<OpenTelemetryStreamItem> stream, CancellationToken cancellationToken)
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
                var completed = await Task.WhenAny(moveNext, Task.Delay(HeartbeatInterval, heartbeatCts.Token));
                if (completed != moveNext)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await WriteAsync(response, formatter.Heartbeat(), cancellationToken);
                    await response.Body.FlushAsync(cancellationToken);
                    continue;
                }

                await heartbeatCts.CancelAsync();
                if (!await moveNext)
                    break;

                await WriteAsync(response, formatter.Format(enumerator.Current), cancellationToken);
                await response.Body.FlushAsync(cancellationToken);
                moveNext = enumerator.MoveNextAsync().AsTask();
            }
        }
        finally
        {
            await DisposeAfterPendingMoveNextAsync(enumerator, moveNext, streamCts, PendingMoveNextCleanupTimeout);
        }
    }

    private static async Task DisposeAfterPendingMoveNextAsync(
        IAsyncEnumerator<OpenTelemetryStreamItem> enumerator,
        Task<bool>? moveNext,
        CancellationTokenSource streamCts,
        TimeSpan timeout)
    {
        if (moveNext is { IsCompleted: false })
        {
            await streamCts.CancelAsync();
            var completed = await Task.WhenAny(moveNext, Task.Delay(timeout));
            if (completed == moveNext)
            {
                try
                { await moveNext; }
                catch (OperationCanceledException) when (streamCts.IsCancellationRequested) { }
            }
        }

        var pending = moveNext is { IsCompleted: false };
        try
        { await enumerator.DisposeAsync(); }
        catch (NotSupportedException) when (pending) { }
    }

    private static ValueTask WriteAsync(HttpResponse response, string value, CancellationToken cancellationToken) =>
        response.Body.WriteAsync(Encoding.UTF8.GetBytes(value), cancellationToken);
}
