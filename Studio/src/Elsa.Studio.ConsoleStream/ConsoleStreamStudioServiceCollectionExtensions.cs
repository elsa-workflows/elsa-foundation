using ConsoleLogStreaming.AspNetCore.DependencyInjection;
using ConsoleLogStreaming.Core.DependencyInjection;
using Elsa.Studio.ConsoleStream.Handlers;
using Elsa.Studio.ConsoleStream.Options;
using Elsa.Studio.Core.Events;
using Elsa.Studio.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Studio.ConsoleStream;

public static class ConsoleStreamStudioServiceCollectionExtensions
{
    public static IServiceCollection AddConsoleStreamStudio(
        this IServiceCollection services,
        ConsoleStreamStudioOptions? options = null)
    {
        var consoleOptions = options ?? new ConsoleStreamStudioOptions();
        var endpointPrefix = consoleOptions.EndpointPrefix.TrimEnd('/');

        services.AddConsoleLogStreamingHost(hostOptions =>
        {
            hostOptions.ServiceName = "elsa-studio";
            hostOptions.SourceDisplayName = "Elsa Studio";
            hostOptions.RecentCapacity = consoleOptions.RecentCapacity;
            hostOptions.MaxRecentQuerySize = consoleOptions.MaxRecentQuerySize;
            hostOptions.PreserveAnsi = consoleOptions.PreserveAnsi;
        });

        services.AddConsoleLogStreamingAspNetCore(aspNetCoreOptions =>
        {
            aspNetCoreOptions.RecentPath = $"{endpointPrefix}/recent";
            aspNetCoreOptions.SourcesPath = $"{endpointPrefix}/sources";
            aspNetCoreOptions.HubPath = $"{endpointPrefix}/hub";
        });

        return services.AddStudioEventHandler<OnStudioModuleManifestsCollecting, ContributeConsoleStreamStudioModule>();
    }
}
