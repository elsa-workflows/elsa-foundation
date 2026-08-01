using Elsa.Api.FastEndpoints.Abstractions;
using Elsa.Api.FastEndpoints.Extensions;
using Elsa.Diagnostics.StructuredLogs.Core.Contracts;
using Elsa.Diagnostics.StructuredLogs.Core.Exceptions;
using Elsa.Diagnostics.StructuredLogs.Core.Models;
using Elsa.Diagnostics.StructuredLogs.Core.Options;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Elsa.Diagnostics.StructuredLogs.Endpoints;

/// <summary>
/// Streams captured entries to a client as Server-Sent Events. Supports filtered subscriptions, periodic
/// heartbeats, and opaque <c>Last-Event-ID</c> resume from the bound store. Thin adapter: framing lives in
/// <see cref="StructuredLogSseFormatter"/>, resume lookup in the store, and parsing in the binder.
/// </summary>
internal sealed class StreamEndpoint(
    IStructuredLogLiveFeed feed,
    IStructuredLogStore store,
    StructuredLogFilterBinder binder,
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

        using var liveCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var pageSize = Math.Max(1, options.Value.MaxRecentQuerySize);
        StructuredLogReplayCursor? position;
        var isResume = request.Headers.TryGetValue("Last-Event-ID", out var lastEventId);
        if (isResume)
        {
            if (!StructuredLogReplayCursor.TryParse(lastEventId.ToString(), out var cursor))
            {
                await RejectCursorAsync(liveCts, ct);
                return;
            }

            position = cursor.Value;
        }
        else
        {
            // Capture the durable boundary first. A commit racing before subscription is still strictly
            // after this cursor and therefore appears in the first storage read.
            position = await store.GetTailCursorAsync(ct);
        }
        if (position is { IsValid: false })
            throw new StructuredLogsException("The structured log store returned an invalid tail cursor.");

        // This process-local subscription is only a wake hint. Its entries are never sent as payload;
        // every SSE record comes from a bounded durable read, so commits from other processes and local
        // completions observed out of order still follow the provider's single committed cursor order.
        var live = feed.Subscribe(filter, liveCts.Token);
        StructuredLogReadPage firstPage;
        try
        {
            firstPage = await store.ReadAfterAsync(position, filter, pageSize, ct);
            ValidatePage(firstPage);
        }
        catch (StructuredLogReplayCursorUnavailableException)
        {
            await RejectCursorAsync(liveCts, ct);
            return;
        }

        var response = HttpContext.Response;
        response.StartServerSentEventStream();
        await response.Body.FlushAsync(ct);

        try
        {
            await streamWriter.StreamAsync(
                response,
                DurableTail(
                    store,
                    live,
                    filter,
                    position,
                    firstPage,
                    pageSize,
                    options.Value.TailPollInterval <= TimeSpan.Zero
                        ? TimeSpan.FromMilliseconds(10)
                        : options.Value.TailPollInterval,
                    ct),
                ct);
        }
        catch (OperationCanceledException)
        {
            // The client disconnected; nothing to clean up beyond the feed unsubscribe (handled by the store).
        }
    }

    private static async IAsyncEnumerable<StructuredLogStreamItem> DurableTail(
        IStructuredLogStore store,
        IAsyncEnumerable<StructuredLogStreamItem> live,
        StructuredLogFilter filter,
        StructuredLogReplayCursor? position,
        StructuredLogReadPage firstPage,
        int pageSize,
        TimeSpan pollInterval,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        IAsyncEnumerator<StructuredLogStreamItem>? wakeEnumerator = null;
        try
        {
            wakeEnumerator = live.GetAsyncEnumerator(cancellationToken);
        }
        catch
        {
            // Wake hints are an optimization. Durable polling remains authoritative if the local feed fails.
        }

        Task<bool>? pendingWake = null;
        var page = firstPage;
        var feedCompleted = wakeEnumerator is null;
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ValidatePage(page);
                foreach (var entry in page.Entries)
                {
                    yield return StructuredLogStreamItem.ForEntry(entry);
                }

                position = page.NextCursor;
                if (page.HasMore)
                {
                    page = await store.ReadAfterAsync(position, filter, pageSize, cancellationToken);
                    ValidatePage(page);
                    continue;
                }

                if (!feedCompleted && pendingWake is null)
                {
                    try
                    {
                        pendingWake = wakeEnumerator!.MoveNextAsync().AsTask();
                    }
                    catch
                    {
                        feedCompleted = true;
                    }
                }
                var delay = Task.Delay(pollInterval, cancellationToken);
                if (pendingWake is null)
                {
                    await delay;
                }
                else if (await Task.WhenAny(pendingWake, delay) == pendingWake)
                {
                    try
                    {
                        feedCompleted = !await pendingWake;
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch
                    {
                        feedCompleted = true;
                    }

                    pendingWake = null;
                }

                page = await store.ReadAfterAsync(position, filter, pageSize, cancellationToken);
                ValidatePage(page);
            }
        }
        finally
        {
            var wakeStillPending = false;
            if (pendingWake is not null)
            {
                var completed = await Task.WhenAny(pendingWake, Task.Delay(TimeSpan.FromMilliseconds(100)));
                wakeStillPending = completed != pendingWake;
                if (!wakeStillPending)
                {
                    try
                    {
                        await pendingWake;
                    }
                    catch
                    {
                    }
                }
            }

            if (wakeEnumerator is not null && !wakeStillPending)
            {
                try
                {
                    await wakeEnumerator.DisposeAsync();
                }
                catch
                {
                }
            }
        }
    }

    private static void ValidatePage(StructuredLogReadPage page)
    {
        ArgumentNullException.ThrowIfNull(page);
        var requiresNextCursor = page.HasMore || page.Entries.Count != 0;
        if ((requiresNextCursor && page.NextCursor is not { IsValid: true }) ||
            page.NextCursor is { IsValid: false } ||
            page.Entries.Any(entry => entry.ReplayCursor is not { IsValid: true }))
        {
            throw new StructuredLogsException("The structured log store returned an invalid committed cursor.");
        }
    }

    private async Task RejectCursorAsync(CancellationTokenSource liveCts, CancellationToken cancellationToken)
    {
        await liveCts.CancelAsync();
        await Send.StringAsync(
            "The structured log replay cursor is unavailable.",
            StatusCodes.Status409Conflict,
            cancellation: cancellationToken);
    }
}
