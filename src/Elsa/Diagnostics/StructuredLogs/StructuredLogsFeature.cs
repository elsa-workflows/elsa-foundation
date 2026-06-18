using CShells.Features;
using Elsa.Api.FastEndpoints;
using Elsa.Diagnostics.StructuredLogs.Capture;
using Elsa.Diagnostics.StructuredLogs.Core.Contracts;
using Elsa.Diagnostics.StructuredLogs.Core.Options;
using Elsa.Diagnostics.StructuredLogs.Endpoints;
using Elsa.Diagnostics.StructuredLogs.Live;
using Elsa.Diagnostics.StructuredLogs.Sources;
using Elsa.Diagnostics.StructuredLogs.Storage;
using Elsa.Platform.PackageManifest.Generator.Hints;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace Elsa.Diagnostics.StructuredLogs;

/// <summary>
/// Captures host log events into an in-memory store and exposes them over HTTP (recent history, known
/// sources) and Server-Sent Events (live tail). Persistence is deferred behind the store contract.
/// </summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Diagnostics")]
[ManifestFeatureCategory("Observability")]
[ShellFeature(
    name: "DiagnosticsStructuredLogs",
    DisplayName = "Diagnostics: Structured Logs",
    Description = "Captures structured log entries and exposes them for live tailing and recent-history queries."
)]
public class StructuredLogsFeature : FastEndpointsFeatureBase
{
    [ManifestSetting(DisplayName = "Minimum level", Description = "Lowest log level captured into the diagnostics pipeline.", Category = "Diagnostics", DefaultValue = "Information")]
    public string MinimumLevel { get; set; } = LogLevel.Information.ToString();

    [ManifestSetting(DisplayName = "Buffer capacity", Description = "Maximum number of recent entries retained in memory.", Category = "Diagnostics", DefaultValue = "2000")]
    public int BufferCapacity { get; set; } = 2000;

    [ManifestSetting(DisplayName = "Service name", Description = "Identifies the local log source. Defaults to the process name.", Category = "Diagnostics")]
    public string? ServiceName { get; set; }

    [ManifestSetting(DisplayName = "Source display name", Description = "UI label for the local log source.", Category = "Diagnostics")]
    public string? SourceDisplayName { get; set; }

    public override void ConfigureServices(IServiceCollection services)
    {
        base.ConfigureServices(services);

        var minimumLevel = Enum.TryParse<LogLevel>(MinimumLevel, ignoreCase: true, out var parsed)
            ? parsed
            : LogLevel.Information;

        services.Configure<StructuredLogsOptions>(options =>
        {
            options.MinimumLevel = minimumLevel;
            options.BufferCapacity = BufferCapacity;
        });

        services.AddSingleton<InMemoryStructuredLogStore>();
        services.TryAddSingleton<IStructuredLogStore>(sp => sp.GetRequiredService<InMemoryStructuredLogStore>());

        services.AddSingleton<InMemoryStructuredLogLiveFeed>();
        services.AddSingleton<IStructuredLogLiveFeed>(sp => sp.GetRequiredService<InMemoryStructuredLogLiveFeed>());
        services.AddSingleton<IStructuredLogLivePublisher>(sp => sp.GetRequiredService<InMemoryStructuredLogLiveFeed>());

        services.AddSingleton<IStructuredLogSink>(sp => new StructuredLogSink(
            sp.GetRequiredService<IStructuredLogStore>(),
            sp.GetRequiredService<IStructuredLogLivePublisher>()));

        services.AddSingleton<IStructuredLogSourceProvider>(_ => new LocalStructuredLogSourceProvider(ServiceName, SourceDisplayName));

        services.AddSingleton<StructuredLogEntrySerializer>();
        services.AddSingleton<StructuredLogSseFormatter>();
        services.AddSingleton<StructuredLogFilterBinder>();

        services.TryAddEnumerable(ServiceDescriptor.Singleton<ILoggerProvider, StructuredLogCaptureProvider>());
    }
}
