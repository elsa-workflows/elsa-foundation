using CShells.Features;
using Elsa.Api.FastEndpoints;
using Elsa.Diagnostics.OpenTelemetry.Core.Options;
using Elsa.Diagnostics.OpenTelemetry.Endpoints;
using Elsa.Diagnostics.OpenTelemetry.Extensions;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Diagnostics.OpenTelemetry;

/// <summary>
/// Collects OpenTelemetry signals (traces, metrics, logs) over OTLP/HTTP protobuf into capacity-bounded
/// in-memory stores and exposes them to Elsa Studio over HTTP query endpoints and a Server-Sent Events live
/// stream. Sensitive attribute names and text patterns are redacted on ingestion. Persistence and an
/// external transport are deferred behind the store/feed contracts; gRPC ingestion is deferred behind an
/// option.
/// </summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Diagnostics")]
[ManifestFeatureCategory("Observability")]
[ShellFeature(
    name: "DiagnosticsOpenTelemetry",
    DisplayName = "Diagnostics: OpenTelemetry",
    Description = "Collects OpenTelemetry traces, metrics, and logs over OTLP/HTTP and exposes them for querying and live streaming."
)]
public class OpenTelemetryFeature : FastEndpointsFeatureBase
{
    [ManifestSetting(DisplayName = "Trace capacity", Description = "Maximum number of recent traces retained in memory.", Category = "Diagnostics", DefaultValue = "5000")]
    public int TraceCapacity { get; set; } = 5_000;

    [ManifestSetting(DisplayName = "Span capacity", Description = "Maximum number of recent spans retained in memory.", Category = "Diagnostics", DefaultValue = "25000")]
    public int SpanCapacity { get; set; } = 25_000;

    [ManifestSetting(DisplayName = "Metric point capacity", Description = "Maximum number of recent metric points retained in memory.", Category = "Diagnostics", DefaultValue = "25000")]
    public int MetricPointCapacity { get; set; } = 25_000;

    [ManifestSetting(DisplayName = "Log record capacity", Description = "Maximum number of recent OTLP log records retained in memory.", Category = "Diagnostics", DefaultValue = "10000")]
    public int LogRecordCapacity { get; set; } = 10_000;

    [ManifestSetting(DisplayName = "Resource capacity", Description = "Maximum number of recent telemetry resources retained in memory.", Category = "Diagnostics", DefaultValue = "500")]
    public int ResourceCapacity { get; set; } = 500;

    [ManifestSetting(DisplayName = "Subscriber channel capacity", Description = "Maximum queued live updates per subscriber before updates are dropped.", Category = "Diagnostics", DefaultValue = "1000")]
    public int SubscriberChannelCapacity { get; set; } = 1_000;

    [ManifestSetting(DisplayName = "Max query size", Description = "Upper clamp applied to a query result size.", Category = "Diagnostics", DefaultValue = "1000")]
    public int MaxQuerySize { get; set; } = 1_000;

    [ManifestSetting(DisplayName = "Max HTTP request body size", Description = "Maximum OTLP HTTP/protobuf request body size in bytes.", Category = "Diagnostics", DefaultValue = "10485760")]
    public long MaxHttpRequestBodySize { get; set; } = 10 * 1024 * 1024;

    [ManifestSetting(DisplayName = "API key", Description = "Optional API key required on the OTLP collector endpoints. When empty, loopback ingestion is allowed unauthenticated.", Category = "Diagnostics")]
    public string? ApiKey { get; set; }

    [ManifestSetting(DisplayName = "API key header", Description = "Header name carrying the OTLP ingestion API key.", Category = "Diagnostics", DefaultValue = "x-otlp-api-key")]
    public string ApiKeyHeaderName { get; set; } = "x-otlp-api-key";

    [ManifestSetting(DisplayName = "Allow unauthenticated loopback", Description = "Allow OTLP ingestion from loopback addresses without an API key.", Category = "Diagnostics", DefaultValue = "true")]
    public bool AllowUnauthenticatedLoopback { get; set; } = true;

    public override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        services.AddOpenTelemetryDiagnosticsServices(ConfigureOptions);

        services.AddSingleton<OpenTelemetryStreamItemSerializer>();
        services.AddSingleton<OpenTelemetrySseFormatter>();
        services.AddSingleton<OpenTelemetryTraceFilterBinder>();
    }

    private void ConfigureOptions(OpenTelemetryDiagnosticsOptions options)
    {
        options.TraceCapacity = TraceCapacity;
        options.SpanCapacity = SpanCapacity;
        options.MetricPointCapacity = MetricPointCapacity;
        options.LogRecordCapacity = LogRecordCapacity;
        options.ResourceCapacity = ResourceCapacity;
        options.SubscriberChannelCapacity = SubscriberChannelCapacity;
        options.MaxQuerySize = MaxQuerySize;
        options.MaxHttpRequestBodySize = MaxHttpRequestBodySize;
        options.ApiKey = ApiKey;
        options.ApiKeyHeaderName = ApiKeyHeaderName;
        options.AllowUnauthenticatedLoopback = AllowUnauthenticatedLoopback;
    }
}
