using Elsa.Diagnostics.OpenTelemetry.Core.Options;
using Elsa.Diagnostics.OpenTelemetry.Ingestion;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Elsa.Diagnostics.OpenTelemetry.Extensions;

/// <summary>ASP.NET Core route composition for the OTLP/HTTP protobuf receiver.</summary>
public static class OpenTelemetryEndpointRouteBuilderExtensions
{
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
        endpoints.MapPost(
                $"{basePath}/{GetRouteSegment(signal)}",
                (HttpContext context, OtlpHttpIngestionHandler handler, CancellationToken cancellationToken) =>
                    handler.HandleAsync(context, signal, cancellationToken))
            .AllowAnonymous();
    }
}
