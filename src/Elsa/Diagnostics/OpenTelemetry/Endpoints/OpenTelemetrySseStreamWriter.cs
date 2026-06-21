using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Microsoft.AspNetCore.Http;

namespace Elsa.Diagnostics.OpenTelemetry.Endpoints;

/// <summary>
/// Writes OpenTelemetry live feed items to an HTTP response as Server-Sent Events.
/// </summary>
public sealed class OpenTelemetrySseStreamWriter
{
    private static readonly TimeSpan DefaultHeartbeatInterval = TimeSpan.FromSeconds(15);
    private readonly OpenTelemetrySseFormatter _formatter;
    private readonly TimeSpan _heartbeatInterval;

    public OpenTelemetrySseStreamWriter(OpenTelemetrySseFormatter formatter) : this(formatter, DefaultHeartbeatInterval)
    {
    }

    public OpenTelemetrySseStreamWriter(OpenTelemetrySseFormatter formatter, TimeSpan heartbeatInterval)
    {
        ArgumentNullException.ThrowIfNull(formatter);
        if (heartbeatInterval <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(heartbeatInterval), heartbeatInterval, "Heartbeat interval must be positive.");

        _formatter = formatter;
        _heartbeatInterval = heartbeatInterval;
    }

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
            await DisposeAfterPendingMoveNextAsync(enumerator, moveNext, streamCts);
        }
    }

    private static async Task DisposeAfterPendingMoveNextAsync(
        IAsyncEnumerator<OpenTelemetryStreamItem> enumerator,
        Task<bool>? moveNext,
        CancellationTokenSource streamCts)
    {
        if (moveNext is { IsCompleted: false })
        {
            await streamCts.CancelAsync();
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
