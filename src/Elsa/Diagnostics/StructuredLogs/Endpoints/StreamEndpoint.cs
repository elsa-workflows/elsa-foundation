using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Diagnostics.StructuredLogs.Core.Contracts;
using Elsa.Diagnostics.StructuredLogs.Core.Exceptions;
using Elsa.Diagnostics.StructuredLogs.Core.Models;
using Elsa.Diagnostics.StructuredLogs.Core.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Elsa.Diagnostics.StructuredLogs.Endpoints;

/// <summary>
/// Streams captured entries to a client as Server-Sent Events. Supports filtered subscriptions, periodic
/// heartbeats, and <c>Last-Event-ID</c> resume from the in-memory buffer. Thin adapter: framing lives in
/// <see cref="StructuredLogSseFormatter"/>, resume lookup in the store, and parsing in the binder.
/// </summary>
internal sealed class StreamEndpoint(
    IStructuredLogLiveFeed feed,
    IStructuredLogStore store,
    StructuredLogFilterBinder binder,
    StructuredLogSseFormatter formatter,
    StructuredLogSseStreamWriter streamWriter,
    IOptions<StructuredLogsOptions> options) : ElsaEndpointWithoutRequest
{
    public override void Configure()
    {
        Get(options.Value.StreamPath);
        ConfigurePermissions(StructuredLogsPermissions.Policy);
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        var request = HttpContext.Request;

        StructuredLogFilter filter;
        try
        {
            filter = binder.Bind(request.Query["minLevel"], request.Query["category"], request.Query["source"], take: null);
        }
        catch (InvalidLogQueryException e)
        {
            await Send.StringAsync(e.Message, 400, cancellation: ct);
            return;
        }

        var response = HttpContext.Response;
        response.StatusCode = 200;
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        response.Headers.Connection = "keep-alive";
        response.Headers["X-Accel-Buffering"] = "no";
        await response.Body.FlushAsync(ct);

        // Subscribe BEFORE querying history: the live feed registers subscribers eagerly, so entries
        // emitted while the history query is in flight are buffered by the subscription instead of being
        // lost in the gap between snapshot and subscription (issue #411). Entries the replay already
        // delivered are skipped by sequence when the buffered live stream is drained.
        var live = feed.Subscribe(filter, ct);

        var lastReplayedSequence = long.MinValue;
        if (long.TryParse(request.Headers["Last-Event-ID"], out var afterSequence))
        {
            lastReplayedSequence = afterSequence;
            foreach (var entry in await store.GetAfterAsync(afterSequence, filter, ct))
            {
                await response.WriteAsync(formatter.FormatEntry(entry), ct);
                lastReplayedSequence = Math.Max(lastReplayedSequence, entry.Sequence);
            }

            await response.Body.FlushAsync(ct);
        }

        try
        {
            await streamWriter.StreamAsync(response, SkipReplayed(live, lastReplayedSequence), ct);
        }
        catch (OperationCanceledException)
        {
            // The client disconnected; nothing to clean up beyond the feed unsubscribe (handled by the store).
        }
    }

    private static async IAsyncEnumerable<StructuredLogStreamItem> SkipReplayed(
        IAsyncEnumerable<StructuredLogStreamItem> live,
        long lastReplayedSequence)
    {
        await foreach (var item in live)
        {
            if (item.Entry is { } entry && entry.Sequence <= lastReplayedSequence)
                continue;

            yield return item;
        }
    }
}
