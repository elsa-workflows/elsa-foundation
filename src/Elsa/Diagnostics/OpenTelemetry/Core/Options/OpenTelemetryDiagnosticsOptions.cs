namespace Elsa.Diagnostics.OpenTelemetry.Core.Options;

public class OpenTelemetryDiagnosticsOptions
{
    public int TraceCapacity { get; set; } = 5_000;
    public int SpanCapacity { get; set; } = 25_000;
    public int MetricPointCapacity { get; set; } = 25_000;
    public int LogRecordCapacity { get; set; } = 10_000;
    public int ResourceCapacity { get; set; } = 500;
    public int MetricInstrumentCapacity { get; set; } = 5_000;
    public int SubscriberChannelCapacity { get; set; } = 1_000;
    public int MaxQuerySize { get; set; } = 1_000;
    public long MaxHttpRequestBodySize { get; set; } = 10 * 1024 * 1024;
    public string HttpEndpointPath { get; set; } = "/elsa/otlp/v1";

    /// <summary>The Server-Sent Events GET path for the live telemetry feed.</summary>
    public string StreamPath { get; set; } = "/_elsa/studio/diagnostics/opentelemetry/stream";

    public string? GrpcEndpointPath { get; set; }
    public string GrpcDisabledReason { get; set; } = "gRPC ingestion is not enabled for this host.";
    public bool EnableGrpc { get; set; }
    public string? ApiKey { get; set; }
    public string ApiKeyHeaderName { get; set; } = "x-otlp-api-key";

    /// <summary>
    /// When no <see cref="ApiKey"/> is configured, allows unauthenticated ingestion from loopback callers
    /// (developer convenience). Security caveat: if a reverse proxy forwards external traffic to the app over
    /// loopback without configuring forwarded-headers handling, the app sees the proxy's loopback address and
    /// treats external callers as local. In proxied/production deployments, set an <see cref="ApiKey"/> (or
    /// set this to <c>false</c>) so the anonymous collector endpoints are not reachable by external clients.
    /// </summary>
    public bool AllowUnauthenticatedLoopback { get; set; } = true;

    public ICollection<string> SensitiveNames { get; set; } =
    [
        "authorization",
        "token",
        "password",
        "secret",
        "api-key",
        "apikey",
        "cookie",
        "connection-string",
        "connectionstring"
    ];

    public ICollection<string> SensitiveTextPatterns { get; set; } =
    [
        "(?i)bearer\\s+[A-Za-z0-9._~+/=-]+",
        "(?i)(password|secret|token|api[-_]?key)\\s*[=:]\\s*[^\\s,;]+",
        "(?i)(AccountKey|SharedAccessKey)=([^;\\s]+)"
    ];

    public TimeSpan SensitiveTextPatternTimeout { get; set; } = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// How long a durable store's lifecycle stop waits for the background drain loop to persist buffered
    /// telemetry on graceful shutdown before hard-cancelling. <see cref="IAsyncDisposable.DisposeAsync"/>
    /// remains a fallback for hosts that do not invoke the explicit lifecycle. This window is the adapter-owned
    /// bound on shutdown drain time, so size it below the host's budget (for example, the container termination
    /// grace period). Negative values are clamped to zero; <see cref="TimeSpan.Zero"/> disables the graceful wait.
    /// </summary>
    public TimeSpan ShutdownDrainTimeout { get; set; } = TimeSpan.FromSeconds(10);
}
