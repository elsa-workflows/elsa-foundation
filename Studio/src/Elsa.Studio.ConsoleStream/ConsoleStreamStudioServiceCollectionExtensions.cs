using ConsoleLogStreaming.AspNetCore.DependencyInjection;
using ConsoleLogStreaming.Core;
using ConsoleLogStreaming.Core.DependencyInjection;
using ConsoleLogStreaming.Core.Providers;
using Elsa.Studio.ConsoleStream.Handlers;
using Elsa.Studio.Core.Events;
using Elsa.Studio.Core.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Studio.ConsoleStream;

public static class ConsoleStreamStudioServiceCollectionExtensions
{
    private const string EndpointPrefix = "/_elsa/studio/diagnostics/console-logs";
    private const int RecentCapacity = 2_000;
    private const int MaxRecentQuerySize = 2_000;

    /// <summary>
    /// Minimum interval between live-stream batch releases. Bounds the long-polling feedback loop
    /// (one captured stdout line completing one pending poll 1:1) at ~10 poll round-trips per second
    /// even when a host logs per-request at Information — see <see cref="PacedConsoleLogProvider"/>.
    /// </summary>
    internal static readonly TimeSpan StreamReleaseInterval = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Registers the per-shell Studio integration for the console stream: the UI module manifest
    /// contribution. The console-log-streaming <em>host</em> (capture, SignalR hub, HTTP endpoints) is
    /// hosted at the application root instead — see <see cref="AddConsoleStreamStudioHost"/> — so that
    /// long-lived hub connections never capture a shell service provider that is disposed when the shell
    /// is recycled at runtime.
    /// </summary>
    public static IServiceCollection AddConsoleStreamStudio(this IServiceCollection services) =>
        services.AddStudioEventHandler<OnStudioModuleManifestsCollecting, ContributeConsoleStreamStudioModule>();

    /// <summary>
    /// Registers the console-log-streaming host (capture host + SignalR hub + HTTP endpoints) on the
    /// supplied service collection. Call this on the application root container so the hub's lifetime
    /// matches the connection's, not a recyclable shell's.
    /// </summary>
    public static IServiceCollection AddConsoleStreamStudioHost(this IServiceCollection services)
    {
        services.AddConsoleLogStreamingHost(hostOptions =>
        {
            hostOptions.ServiceName = "elsa-studio";
            hostOptions.SourceDisplayName = "Elsa Studio";
            hostOptions.RecentCapacity = RecentCapacity;
            hostOptions.MaxRecentQuerySize = MaxRecentQuerySize;
            hostOptions.PreserveAnsi = true;
        });

        services.AddConsoleLogStreamingAspNetCore(aspNetCoreOptions =>
        {
            aspNetCoreOptions.RecentPath = $"{EndpointPrefix}/recent";
            aspNetCoreOptions.SourcesPath = $"{EndpointPrefix}/sources";
            aspNetCoreOptions.HubPath = $"{EndpointPrefix}/hub";
        });

        // Wrap the default in-memory provider so live streams release in time-gated batches. The
        // process-wide host builds its provider from this factory on first use; the call is a no-op
        // (returns false) if a provider factory was already configured or the host is running, so
        // repeated registration is safe. Consumers that need a custom IConsoleLogProvider should
        // configure it before calling this method and are then expected to pace it themselves.
        ConsoleLogStreamingHost.ConfigureProvider(context => new PacedConsoleLogProvider(
            new InMemoryConsoleLogProvider(context.Options, context.RedactionPipeline, context.SourceRegistry),
            StreamReleaseInterval));

        return services;
    }

    /// <summary>
    /// Registers the console-log-streaming host on the application root only when the ConsoleStream
    /// feature is enabled in shell configuration, reusing the same enablement check that gates the
    /// capture hook. Because this is evaluated once at startup, the feature must be enabled in
    /// <c>shells.json</c> at boot (consistent with the capture hook); enabling it only at runtime does
    /// not host the hub. Pair with <see cref="Extensions.ConsoleStreamEndpointRouteBuilderExtensions.MapConsoleStreamStudioIfEnabled"/>.
    /// </summary>
    public static IServiceCollection AddConsoleStreamStudioHostIfEnabled(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        if (ConsoleStreamHookInstaller.IsFeatureEnabled(configuration))
            services.AddConsoleStreamStudioHost();

        return services;
    }
}
