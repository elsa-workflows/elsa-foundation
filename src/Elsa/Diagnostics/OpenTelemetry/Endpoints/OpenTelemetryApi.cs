using System.Text.Json;
using Elsa.Api.AspNetCore;
using Elsa.Diagnostics.OpenTelemetry.Core.Contracts;
using Elsa.Diagnostics.OpenTelemetry.Core.Models;
using Elsa.Diagnostics.OpenTelemetry.Core.Options;
using Elsa.Diagnostics.OpenTelemetry.Permissions;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace Elsa.Diagnostics.OpenTelemetry.Endpoints;

/// <summary>Maps the OpenTelemetry query and live-stream surface with owner-local Minimal APIs.</summary>
public static class OpenTelemetryApi
{
    private const string QueryTag = "OpenTelemetry";

    public static void MapOpenTelemetryApi(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var description = typeof(RequestDelegate).GetMethod(nameof(RequestDelegate.Invoke))
            ?? throw new InvalidOperationException("RequestDelegate.Invoke metadata is unavailable.");

        MapPost<OpenTelemetryResourceFilter, OpenTelemetryResourceResult>(endpoints,
            "/diagnostics/opentelemetry/resources/search", "OpenTelemetryResourcesSearch", description,
            static (context, filter, cancellationToken) => context.RequestServices.GetRequiredService<IOpenTelemetryProvider>().GetResourcesAsync(filter, cancellationToken),
            OpenTelemetryJsonContext.Default.OpenTelemetryResourceFilter, OpenTelemetryJsonContext.Default.OpenTelemetryResourceResult);
        MapPost<OpenTelemetryTraceFilter, OpenTelemetryTraceResult>(endpoints,
            "/diagnostics/opentelemetry/traces/search", "OpenTelemetryTracesSearch", description,
            static (context, filter, cancellationToken) => context.RequestServices.GetRequiredService<IOpenTelemetryProvider>().GetTracesAsync(filter, cancellationToken),
            OpenTelemetryJsonContext.Default.OpenTelemetryTraceFilter, OpenTelemetryJsonContext.Default.OpenTelemetryTraceResult);
        MapPost<OpenTelemetryMetricFilter, OpenTelemetryMetricResult>(endpoints,
            "/diagnostics/opentelemetry/metrics/search", "OpenTelemetryMetricsSearch", description,
            static (context, filter, cancellationToken) => context.RequestServices.GetRequiredService<IOpenTelemetryProvider>().GetMetricsAsync(filter, cancellationToken),
            OpenTelemetryJsonContext.Default.OpenTelemetryMetricFilter, OpenTelemetryJsonContext.Default.OpenTelemetryMetricResult);
        MapPost<OpenTelemetryLogFilter, OpenTelemetryLogResult>(endpoints,
            "/diagnostics/opentelemetry/logs/search", "OpenTelemetryLogsSearch", description,
            static (context, filter, cancellationToken) => context.RequestServices.GetRequiredService<IOpenTelemetryProvider>().GetLogsAsync(filter, cancellationToken),
            OpenTelemetryJsonContext.Default.OpenTelemetryLogFilter, OpenTelemetryJsonContext.Default.OpenTelemetryLogResult);

        MapGet(endpoints, "/diagnostics/opentelemetry/traces/{traceId}", "OpenTelemetryTraceDetail", description,
            async context =>
            {
                var traceId = context.Request.RouteValues["traceId"]?.ToString() ?? string.Empty;
                var result = await context.RequestServices.GetRequiredService<IOpenTelemetryProvider>().GetTraceAsync(traceId, context.RequestAborted);
                if (result is null)
                {
                    await Results.NotFound().ExecuteAsync(context);
                    return;
                }

                await Results.Json(result, OpenTelemetryJsonContext.Default.OpenTelemetryTraceDetail).ExecuteAsync(context);
            },
            new ProducesResponseTypeMetadata(StatusCodes.Status200OK, typeof(OpenTelemetryTraceDetail), ["application/json"]),
            new ProducesResponseTypeMetadata(StatusCodes.Status404NotFound, typeof(void), []));

        MapGet(endpoints, "/diagnostics/opentelemetry/storage", "OpenTelemetryStorage", description,
            async context =>
            {
                var result = await context.RequestServices.GetRequiredService<IOpenTelemetryProvider>().GetStorageDiagnosticsAsync(context.RequestAborted);
                await Results.Json(result, OpenTelemetryJsonContext.Default.OpenTelemetryStorageDiagnostics).ExecuteAsync(context);
            },
            new ProducesResponseTypeMetadata(StatusCodes.Status200OK, typeof(OpenTelemetryStorageDiagnostics), ["application/json"]));

        MapGet(endpoints, "/diagnostics/opentelemetry/collector-configuration", "OpenTelemetryCollectorConfiguration", description,
            async context =>
            {
                var result = await context.RequestServices.GetRequiredService<IOpenTelemetryProvider>().GetCollectorConfigurationAsync(context.RequestAborted);
                await Results.Json(result, OpenTelemetryJsonContext.Default.CollectorConfiguration).ExecuteAsync(context);
            },
            new ProducesResponseTypeMetadata(StatusCodes.Status200OK, typeof(CollectorConfiguration), ["application/json"]));

        endpoints.MapGet(
                endpoints.ServiceProvider.GetRequiredService<IOptions<OpenTelemetryDiagnosticsOptions>>().Value.StreamPath,
                HandleStreamAsync)
            .WithMetadata(description, new EndpointNameMetadata("OpenTelemetryStream"), new TagsAttribute(QueryTag),
                new ProducesResponseTypeMetadata(StatusCodes.Status200OK, typeof(OpenTelemetryStreamItem), ["text/event-stream"]),
                Unauthorized(), Forbidden())
            .WithOwner(OpenTelemetryPermissions.OwnerId)
            .WithAuthoringModel(EndpointAuthoringModels.MinimalApi)
            .RequirePermission(OpenTelemetryPermissions.Read);
    }

    private static void MapPost<TFilter, TResult>(
        IEndpointRouteBuilder endpoints,
        string route,
        string operationId,
        System.Reflection.MethodInfo description,
        Func<HttpContext, TFilter, CancellationToken, ValueTask<TResult>> execute,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<TFilter> filterInfo,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<TResult> resultInfo)
        where TFilter : class
    {
        endpoints.MapPost(route, async context =>
            {
                TFilter? filter;
                try
                {
                    filter = await context.Request.ReadFromJsonAsync(filterInfo, context.RequestAborted);
                }
                catch (JsonException)
                {
                    await Results.BadRequest().ExecuteAsync(context);
                    return;
                }

                if (filter is null)
                {
                    await Results.BadRequest().ExecuteAsync(context);
                    return;
                }

                var result = await execute(context, filter, context.RequestAborted);
                await Results.Json(result, resultInfo).ExecuteAsync(context);
            })
            .WithMetadata(description, new EndpointNameMetadata(operationId), new TagsAttribute(QueryTag),
                new AcceptsMetadata(["application/json"], typeof(TFilter), false),
                new ProducesResponseTypeMetadata(StatusCodes.Status200OK, typeof(TResult), ["application/json"]),
                Unauthorized(), Forbidden())
            .WithOwner(OpenTelemetryPermissions.OwnerId)
            .WithAuthoringModel(EndpointAuthoringModels.MinimalApi)
            .RequirePermission(OpenTelemetryPermissions.Read);
    }

    private static void MapGet(
        IEndpointRouteBuilder endpoints,
        string route,
        string operationId,
        System.Reflection.MethodInfo description,
        RequestDelegate handler,
        params object[] responseMetadata)
    {
        endpoints.MapGet(route, handler)
            .WithMetadata([description, new EndpointNameMetadata(operationId), new TagsAttribute(QueryTag), .. responseMetadata, Unauthorized(), Forbidden()])
            .WithOwner(OpenTelemetryPermissions.OwnerId)
            .WithAuthoringModel(EndpointAuthoringModels.MinimalApi)
            .RequirePermission(OpenTelemetryPermissions.Read);
    }

    private static async Task HandleStreamAsync(HttpContext context)
    {
        var services = context.RequestServices;
        var binder = services.GetRequiredService<OpenTelemetryTraceFilterBinder>();
        OpenTelemetryTraceFilter filter;
        try
        {
            filter = binder.Bind(context.Request.Query.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase));
        }
        catch (InvalidTelemetryQueryException exception)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            context.Response.ContentType = "text/plain; charset=utf-8";
            await context.Response.WriteAsync(exception.Message, context.RequestAborted);
            return;
        }

        var response = context.Response;
        response.StatusCode = StatusCodes.Status200OK;
        response.ContentType = "text/event-stream";
        response.Headers.CacheControl = "no-cache";
        response.Headers.Connection = "keep-alive";
        response.Headers["X-Accel-Buffering"] = "no";
        await response.Body.FlushAsync(context.RequestAborted);

        var feed = services.GetRequiredService<IOpenTelemetryLiveFeed>();
        var writer = services.GetRequiredService<OpenTelemetrySseStreamWriter>();
        try
        {
            await writer.StreamAsync(response, feed.SubscribeAsync(filter, context.RequestAborted), context.RequestAborted);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
        }
    }

    private static ProducesResponseTypeMetadata Unauthorized() =>
        new(StatusCodes.Status401Unauthorized, typeof(void), []);

    private static ProducesResponseTypeMetadata Forbidden() =>
        new(StatusCodes.Status403Forbidden, typeof(void), []);
}
