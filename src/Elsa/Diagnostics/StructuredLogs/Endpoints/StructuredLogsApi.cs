using System.Runtime.CompilerServices;
using Elsa.Api.AspNetCore;
using Elsa.Diagnostics.StructuredLogs.Authorization;
using Elsa.Diagnostics.StructuredLogs.Core.Contracts;
using Elsa.Diagnostics.StructuredLogs.Core.Exceptions;
using Elsa.Diagnostics.StructuredLogs.Core.Models;
using Elsa.Diagnostics.StructuredLogs.Core.Options;
using Elsa.Diagnostics.StructuredLogs.Live;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Elsa.Diagnostics.StructuredLogs.Endpoints;

/// <summary>Maps the structured-log diagnostics REST and Server-Sent Events surface.</summary>
public static class StructuredLogsApi
{
    /// <summary>Maps all module-owned structured-log endpoints.</summary>
    public static void MapStructuredLogsApi(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var options = endpoints.ServiceProvider.GetService<IOptions<StructuredLogsOptions>>()?.Value
            ?? new StructuredLogsOptions();
        RequestDelegate recent = HandleRecentAsync;
        RequestDelegate sources = HandleSourcesAsync;
        RequestDelegate stream = HandleStreamAsync;
        var descriptionMethod = typeof(RequestDelegate).GetMethod(nameof(RequestDelegate.Invoke))
            ?? throw new InvalidOperationException("RequestDelegate.Invoke metadata is unavailable.");

        endpoints.MapGet(options.RecentPath, recent)
            .WithOwner(StructuredLogsPermissions.OwnerId)
            .WithAuthoringModel(EndpointAuthoringModels.MinimalApi)
            .RequireAnyPermission(PermissionKey.Wildcard, StructuredLogsPermissions.Policy)
            .WithMetadata(
                descriptionMethod,
                Response(StatusCodes.Status204NoContent),
                Unauthorized(),
                Forbidden());

        endpoints.MapGet(options.SourcesPath, sources)
            .WithOwner(StructuredLogsPermissions.OwnerId)
            .WithAuthoringModel(EndpointAuthoringModels.MinimalApi)
            .RequireAnyPermission(PermissionKey.Wildcard, StructuredLogsPermissions.Policy)
            .WithMetadata(
                descriptionMethod,
                Response(StatusCodes.Status200OK, typeof(IReadOnlyList<LogSource>), "application/json"),
                Unauthorized(),
                Forbidden());

        endpoints.MapGet(options.StreamPath, stream)
            .WithOwner(StructuredLogsPermissions.OwnerId)
            .WithAuthoringModel(EndpointAuthoringModels.MinimalApi)
            .RequireAnyPermission(PermissionKey.Wildcard, StructuredLogsPermissions.Policy)
            .WithMetadata(
                descriptionMethod,
                Response(StatusCodes.Status204NoContent),
                Unauthorized(),
                Forbidden());
    }

    private static async Task HandleRecentAsync(HttpContext context)
    {
        var query = context.Request.Query;
        var binder = context.RequestServices.GetRequiredService<StructuredLogFilterBinder>();
        var store = context.RequestServices.GetRequiredService<IStructuredLogStore>();
        var serializer = context.RequestServices.GetRequiredService<StructuredLogEntrySerializer>();

        StructuredLogFilter filter;
        try
        {
            filter = binder.Bind(query["minLevel"].ToString(), query["category"].ToString(), query["source"].ToString(), query["take"].ToString());
        }
        catch (InvalidLogQueryException exception)
        {
            await WriteTextAsync(context, exception.Message, StatusCodes.Status400BadRequest);
            return;
        }

        var entries = await store.GetRecentAsync(filter, context.RequestAborted);
        context.Response.StatusCode = StatusCodes.Status200OK;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsync(serializer.SerializeMany(entries), context.RequestAborted);
    }

    private static async Task HandleSourcesAsync(HttpContext context)
    {
        var sourceProvider = context.RequestServices.GetRequiredService<IStructuredLogSourceProvider>();
        await Results.Json(sourceProvider.GetKnownSources(), contentType: "application/json").ExecuteAsync(context);
    }

    private static async Task HandleStreamAsync(HttpContext context)
    {
        var request = context.Request;
        var services = context.RequestServices;
        var binder = services.GetRequiredService<StructuredLogFilterBinder>();
        var feed = services.GetRequiredService<IStructuredLogLiveFeed>();
        var store = services.GetRequiredService<IStructuredLogStore>();
        var streamWriter = services.GetRequiredService<StructuredLogSseWriter>();
        var options = services.GetRequiredService<IOptions<StructuredLogsOptions>>().Value;

        StructuredLogFilter filter;
        try
        {
            filter = binder.Bind(request.Query["minLevel"].ToString(), request.Query["category"].ToString(), request.Query["source"].ToString(), take: null);
        }
        catch (InvalidLogQueryException exception)
        {
            await WriteTextAsync(context, exception.Message, StatusCodes.Status400BadRequest);
            return;
        }

        using var liveCts = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        var pageSize = Math.Max(1, options.MaxRecentQuerySize);
        StructuredLogReplayCursor? position;
        var isResume = request.Headers.TryGetValue("Last-Event-ID", out var lastEventId);
        if (isResume)
        {
            if (!StructuredLogReplayCursor.TryParse(lastEventId.ToString(), out var cursor))
            {
                await RejectCursorAsync(context, liveCts);
                return;
            }

            position = cursor.Value;
        }
        else
        {
            // Capture the durable boundary first. A commit racing before subscription is still strictly
            // after this cursor and therefore appears in the first storage read.
            position = await store.GetTailCursorAsync(context.RequestAborted);
        }

        if (position is { IsValid: false })
            throw new StructuredLogsException("The structured log store returned an invalid tail cursor.");

        // This process-local subscription is only a wake hint. Its entries are never sent as payload;
        // every SSE record comes from a bounded durable read, so commits from other processes and local
        // completions observed out of order still follow the provider's single committed cursor order.
        var live = feed.Subscribe(filter, liveCts.Token);
        try
        {
            StructuredLogReadPage firstPage;
            try
            {
                firstPage = await store.ReadAfterAsync(position, filter, pageSize, context.RequestAborted);
                ValidatePage(firstPage);
            }
            catch (StructuredLogReplayCursorUnavailableException)
            {
                await RejectCursorAsync(context, liveCts);
                return;
            }

            StartServerSentEventStream(context.Response);
            await context.Response.Body.FlushAsync(context.RequestAborted);

            try
            {
                await streamWriter.StreamAsync(
                    context.Response,
                    DurableTail(
                        store,
                        live,
                        filter,
                        position,
                        firstPage,
                        pageSize,
                        options.TailPollInterval <= TimeSpan.Zero
                            ? TimeSpan.FromMilliseconds(10)
                            : options.TailPollInterval,
                        context.RequestAborted),
                    context.RequestAborted);
            }
            catch (OperationCanceledException)
            {
                // The client disconnected; the outer cleanup cancels the eager wake-feed subscription.
            }
        }
        finally
        {
            await liveCts.CancelAsync();
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
        [EnumeratorCancellation] CancellationToken cancellationToken)
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
                    yield return StructuredLogStreamItem.ForEntry(entry);

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

    private static async Task RejectCursorAsync(HttpContext context, CancellationTokenSource liveCts)
    {
        await liveCts.CancelAsync();
        await WriteTextAsync(context, "The structured log replay cursor is unavailable.", StatusCodes.Status409Conflict);
    }

    private static async Task WriteTextAsync(HttpContext context, string value, int statusCode)
    {
        context.Response.StatusCode = statusCode;
        context.Response.ContentType = "text/plain; charset=utf-8";
        await context.Response.WriteAsync(value, context.RequestAborted);
    }

    private static void StartServerSentEventStream(HttpResponse response)
    {
        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        response.Headers.Connection = "keep-alive";
        response.Headers["X-Accel-Buffering"] = "no";
    }

    private static ProducesResponseTypeMetadata Response(int statusCode) =>
        new(statusCode, typeof(void), []);

    private static ProducesResponseTypeMetadata Response(int statusCode, Type type, string contentType) =>
        new(statusCode, type, [contentType]);

    private static ProducesResponseTypeMetadata Unauthorized() =>
        new(StatusCodes.Status401Unauthorized, typeof(void), []);

    private static ProducesResponseTypeMetadata Forbidden() =>
        new(StatusCodes.Status403Forbidden, typeof(void), []);
}
