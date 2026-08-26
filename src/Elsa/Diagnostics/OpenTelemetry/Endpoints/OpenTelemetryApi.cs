using Elsa.Api.Endpoints;
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
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Elsa.Diagnostics.OpenTelemetry.Endpoints;

/// <summary>Maps the OpenTelemetry query and live-stream surface with owner-local Minimal APIs.</summary>
public static class OpenTelemetryApi
{
    private const string QueryTag = "OpenTelemetry";

    public static void MapOpenTelemetryApi(IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        // The published operation ids predate the naming scheme and the published tag is the plain
        // "OpenTelemetry" query tag — both pinned by the reviewed approval registry — and every
        // operation keeps its own reads, writes, problem shapes, and the SSE stream, so the surface
        // stays on the group's raw seam with per-operation name overrides.
        var api = endpoints.MapModuleEndpoints(
            OpenTelemetryPermissions.OwnerId,
            OpenTelemetryJsonContext.Default,
            tag: QueryTag);

        MapPost<OpenTelemetryResourceFilter, OpenTelemetryResourceResult>(api,
            "/diagnostics/opentelemetry/resources/search", "ResourcesSearch", "OpenTelemetryResourcesSearch",
            static (context, filter, cancellationToken) => context.RequestServices.GetRequiredService<IOpenTelemetryProvider>().GetResourcesAsync(filter, cancellationToken),
            OpenTelemetryJsonContext.Default.OpenTelemetryResourceFilter, OpenTelemetryJsonContext.Default.OpenTelemetryResourceResult);
        MapPost<OpenTelemetryTraceFilter, OpenTelemetryTraceResult>(api,
            "/diagnostics/opentelemetry/traces/search", "TracesSearch", "OpenTelemetryTracesSearch",
            static (context, filter, cancellationToken) => context.RequestServices.GetRequiredService<IOpenTelemetryProvider>().GetTracesAsync(filter, cancellationToken),
            OpenTelemetryJsonContext.Default.OpenTelemetryTraceFilter, OpenTelemetryJsonContext.Default.OpenTelemetryTraceResult);
        MapPost<OpenTelemetryMetricFilter, OpenTelemetryMetricResult>(api,
            "/diagnostics/opentelemetry/metrics/search", "MetricsSearch", "OpenTelemetryMetricsSearch",
            static (context, filter, cancellationToken) => context.RequestServices.GetRequiredService<IOpenTelemetryProvider>().GetMetricsAsync(filter, cancellationToken),
            OpenTelemetryJsonContext.Default.OpenTelemetryMetricFilter, OpenTelemetryJsonContext.Default.OpenTelemetryMetricResult);
        MapPost<OpenTelemetryLogFilter, OpenTelemetryLogResult>(api,
            "/diagnostics/opentelemetry/logs/search", "LogsSearch", "OpenTelemetryLogsSearch",
            static (context, filter, cancellationToken) => context.RequestServices.GetRequiredService<IOpenTelemetryProvider>().GetLogsAsync(filter, cancellationToken),
            OpenTelemetryJsonContext.Default.OpenTelemetryLogFilter, OpenTelemetryJsonContext.Default.OpenTelemetryLogResult);

        api.MapUnboundOperation("GET", "/diagnostics/opentelemetry/traces/{traceId}", "TraceDetail",
                typeof(OpenTelemetryTraceDetail), StatusCodes.Status200OK, null,
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
                name: "OpenTelemetryTraceDetail", documentAuthResponses: false)
            .RequirePermission(OpenTelemetryPermissions.Read)
            // The reviewed approval pins the response-status order 200, 404, 401, 403.
            .WithMetadata(
                new ProducesResponseTypeMetadata(StatusCodes.Status404NotFound, typeof(void), []),
                new ProducesResponseTypeMetadata(StatusCodes.Status401Unauthorized, typeof(void), []),
                new ProducesResponseTypeMetadata(StatusCodes.Status403Forbidden, typeof(void), []));

        api.MapUnboundOperation("GET", "/diagnostics/opentelemetry/storage", "Storage",
                typeof(OpenTelemetryStorageDiagnostics), StatusCodes.Status200OK, null,
                async context =>
                {
                    var result = await context.RequestServices.GetRequiredService<IOpenTelemetryProvider>().GetStorageDiagnosticsAsync(context.RequestAborted);
                    await Results.Json(result, OpenTelemetryJsonContext.Default.OpenTelemetryStorageDiagnostics).ExecuteAsync(context);
                },
                name: "OpenTelemetryStorage")
            .RequirePermission(OpenTelemetryPermissions.Read);

        api.MapUnboundOperation("GET", "/diagnostics/opentelemetry/collector-configuration", "CollectorConfiguration",
                typeof(CollectorConfiguration), StatusCodes.Status200OK, null,
                async context =>
                {
                    var result = await context.RequestServices.GetRequiredService<IOpenTelemetryProvider>().GetCollectorConfigurationAsync(context.RequestAborted);
                    await Results.Json(result, OpenTelemetryJsonContext.Default.CollectorConfiguration).ExecuteAsync(context);
                },
                name: "OpenTelemetryCollectorConfiguration")
            .RequirePermission(OpenTelemetryPermissions.Read);

        api.MapUnboundOperation("GET",
                endpoints.ServiceProvider.GetRequiredService<IOptions<OpenTelemetryDiagnosticsOptions>>().Value.StreamPath,
                "Stream", typeof(OpenTelemetryStreamItem), StatusCodes.Status200OK, null, HandleStreamAsync,
                name: "OpenTelemetryStream", successContentType: "text/event-stream")
            .RequirePermission(OpenTelemetryPermissions.Read);
    }

    private static void MapPost<TFilter, TResult>(
        ModuleEndpointGroup api,
        string route,
        string operation,
        string operationId,
        Func<HttpContext, TFilter, CancellationToken, ValueTask<TResult>> execute,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<TFilter> filterInfo,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<TResult> resultInfo)
        where TFilter : class
    {
        api.MapUnboundOperation("POST", route, operation, typeof(TResult), StatusCodes.Status200OK, null,
                async context =>
                {
                    var filter = await ReadJsonAsync(context, filterInfo);
                    if (filter is null)
                        return;

                    var result = await execute(context, filter, context.RequestAborted);
                    await Results.Json(result, resultInfo).ExecuteAsync(context);
                },
                name: operationId)
            .RequirePermission(OpenTelemetryPermissions.Read)
            .WithMetadata(new AcceptsMetadata(["application/json"], typeof(TFilter), false));
    }

    private static async Task<T?> ReadJsonAsync<T>(HttpContext context, JsonTypeInfo<T> typeInfo)
        where T : class
    {
        if (string.IsNullOrWhiteSpace(context.Request.ContentType))
        {
            context.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
            return default;
        }

        try
        {
            var value = await context.Request.ReadFromJsonAsync(typeInfo, context.RequestAborted);
            return value ?? JsonSerializer.Deserialize("{}", typeInfo);
        }
        catch (NotSupportedException)
        {
            context.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
            return default;
        }
        catch (JsonException exception)
        {
            var problem = new OpenTelemetryBindingProblemDetails(
                StatusCodes.Status400BadRequest,
                "One or more errors occurred!",
                new Dictionary<string, string[]> { ["serializerErrors"] = [NormalizeJsonError(exception.Message)] });
            await Results.Json(
                    problem,
                    OpenTelemetryJsonContext.Default.OpenTelemetryBindingProblemDetails,
                    "application/problem+json; charset=utf-8",
                    StatusCodes.Status400BadRequest)
                .ExecuteAsync(context);
            return default;
        }
    }

    private static string NormalizeJsonError(string message) => message.Replace("Path: $ | ", string.Empty, StringComparison.Ordinal);

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

}

internal sealed record OpenTelemetryBindingProblemDetails(
    int StatusCode,
    string Message,
    IReadOnlyDictionary<string, string[]> Errors);
