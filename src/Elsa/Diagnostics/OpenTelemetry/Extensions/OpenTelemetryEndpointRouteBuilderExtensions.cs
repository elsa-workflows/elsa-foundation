using Elsa.Api.AspNetCore;
using Elsa.Diagnostics.OpenTelemetry.Core.Options;
using Elsa.Diagnostics.OpenTelemetry.Ingestion;
using Elsa.Diagnostics.OpenTelemetry.Permissions;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Elsa.Diagnostics.OpenTelemetry.Extensions;

/// <summary>ASP.NET Core route composition for the OTLP/HTTP protobuf receiver.</summary>
public static class OpenTelemetryEndpointRouteBuilderExtensions
{
    private const string TransportCredential = "OTLP API key or loopback";

    /// <summary>
    /// Maps exactly the traces, metrics, and logs OTLP/HTTP POST routes using the configured
    /// <see cref="OpenTelemetryDiagnosticsOptions.HttpEndpointPath"/>.
    /// </summary>
    public static IEndpointRouteBuilder MapOpenTelemetryOtlpReceiver(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        var options = endpoints.ServiceProvider.GetRequiredService<IOptions<OpenTelemetryDiagnosticsOptions>>().Value;
        var basePath = NormalizeBasePath(options.HttpEndpointPath);

        MapSignal(endpoints, basePath, OtlpSignal.Traces);
        MapSignal(endpoints, basePath, OtlpSignal.Metrics);
        MapSignal(endpoints, basePath, OtlpSignal.Logs);

        return endpoints;
    }

    internal static string NormalizeBasePath(string? path)
    {
        var normalized = string.IsNullOrWhiteSpace(path) ? "/elsa/otlp/v1" : path.Trim();
        if (!normalized.StartsWith('/'))
            normalized = $"/{normalized}";

        return normalized.TrimEnd('/');
    }

    internal static string GetRouteSegment(OtlpSignal signal) => signal switch
    {
        OtlpSignal.Traces => "traces",
        OtlpSignal.Metrics => "metrics",
        OtlpSignal.Logs => "logs",
        _ => throw new ArgumentOutOfRangeException(nameof(signal), signal, "Unsupported OTLP signal.")
    };

    private static void MapSignal(IEndpointRouteBuilder endpoints, string basePath, OtlpSignal signal)
    {
        var route = $"{basePath}/{GetRouteSegment(signal)}";
        var builder = signal switch
        {
            OtlpSignal.Traces => endpoints.MapPost(route, (Delegate)HandleTracesAsync),
            OtlpSignal.Metrics => endpoints.MapPost(route, (Delegate)HandleMetricsAsync),
            OtlpSignal.Logs => endpoints.MapPost(route, (Delegate)HandleLogsAsync),
            _ => throw new ArgumentOutOfRangeException(nameof(signal), signal, "Unsupported OTLP signal.")
        };

        builder
            .WithOwner(OpenTelemetryPermissions.OwnerId)
            .WithAuthoringModel(EndpointAuthoringModels.MinimalApi)
            .WithMetadata(
                new EndpointNameMetadata($"OpenTelemetryOtlp{signal}"),
                new TagsAttribute("OpenTelemetry"),
                new ProducesResponseTypeMetadata(StatusCodes.Status204NoContent, typeof(void), []))
            .WithSecurityDisposition(EndpointSecurityDispositionMetadata.HostCredential(TransportCredential, OpenTelemetryPermissions.OwnerId))
            .AllowAnonymous();
    }

    private static Task HandleTracesAsync(HttpContext context) => HandleAsync(context, OtlpSignal.Traces);

    private static Task HandleMetricsAsync(HttpContext context) => HandleAsync(context, OtlpSignal.Metrics);

    private static Task HandleLogsAsync(HttpContext context) => HandleAsync(context, OtlpSignal.Logs);

    private static Task HandleAsync(HttpContext context, OtlpSignal signal) =>
        context.RequestServices.GetRequiredService<OtlpHttpIngestionHandler>().HandleAsync(context, signal, context.RequestAborted);
}
