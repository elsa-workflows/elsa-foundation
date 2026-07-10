using Microsoft.Extensions.Logging;

namespace Elsa.Diagnostics.StructuredLogs.Core.Options;

/// <summary>
/// Configuration for the structured-logs diagnostics feature. Bound under the feature name
/// <c>DiagnosticsStructuredLogs</c>.
/// </summary>
public sealed class StructuredLogsOptions
{
    /// <summary>The minimum level captured into the diagnostics pipeline.</summary>
    public LogLevel MinimumLevel { get; set; } = LogLevel.Information;

    /// <summary>The maximum number of entries retained in the in-memory ring buffer.</summary>
    public int BufferCapacity { get; set; } = 2000;

    /// <summary>The per-subscriber bounded queue size before oldest-queued entries are dropped.</summary>
    public int SubscriberQueueCapacity { get; set; } = 1000;

    /// <summary>The upper clamp applied to a recent-history query size.</summary>
    public int MaxRecentQuerySize { get; set; } = 2000;

    /// <summary>The maximum number of structured properties captured per entry.</summary>
    public int MaxCapturedProperties { get; set; } = 50;

    /// <summary>The maximum scope-chain depth captured per entry.</summary>
    public int MaxCapturedScopeDepth { get; set; } = 10;

    /// <summary>The maximum length of a single captured property/scope value.</summary>
    public int MaxPropertyValueLength { get; set; } = 4096;

    /// <summary>The HTTP GET path for the recent-history query.</summary>
    public string RecentPath { get; set; } = "/_elsa/studio/diagnostics/structured-logs/recent";

    /// <summary>The HTTP GET path for the known-sources query.</summary>
    public string SourcesPath { get; set; } = "/_elsa/studio/diagnostics/structured-logs/sources";

    /// <summary>The Server-Sent Events GET path for the live feed.</summary>
    public string StreamPath { get; set; } = "/_elsa/studio/diagnostics/structured-logs/stream";

    /// <summary>
    /// How long the EF Core store's async disposal waits for the background drain loop to persist buffered
    /// log entries on graceful shutdown before hard-cancelling. <see cref="IAsyncDisposable.DisposeAsync"/>
    /// carries no cancellation token, so this window is the only bound on shutdown drain time — size it
    /// below the host's shutdown budget (e.g. the container termination grace period). Negative values are
    /// clamped to zero; <see cref="TimeSpan.Zero"/> disables the graceful wait entirely.
    /// </summary>
    public TimeSpan ShutdownDrainTimeout { get; set; } = TimeSpan.FromSeconds(10);
}
