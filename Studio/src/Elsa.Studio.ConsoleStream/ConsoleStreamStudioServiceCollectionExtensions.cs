using ConsoleLogStreaming.AspNetCore.DependencyInjection;
using ConsoleLogStreaming.Core.DependencyInjection;
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
