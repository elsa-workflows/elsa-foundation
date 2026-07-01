using ConsoleLogStreaming.AspNetCore.DependencyInjection;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;

namespace Elsa.Studio.ConsoleStream.Extensions;

public static class ConsoleStreamEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapConsoleStreamStudio(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapConsoleLogStreaming();
        return endpoints;
    }

    /// <summary>
    /// Maps the console-log-streaming endpoints (SignalR hub + HTTP routes) only when the ConsoleStream
    /// feature is enabled in shell configuration. Call this on the application root endpoint builder so the
    /// hub is hosted on the root container rather than a recyclable shell scope.
    /// </summary>
    public static IEndpointRouteBuilder MapConsoleStreamStudioIfEnabled(
        this IEndpointRouteBuilder endpoints,
        IConfiguration configuration)
    {
        if (ConsoleStreamHookInstaller.IsFeatureEnabled(configuration))
            endpoints.MapConsoleStreamStudio();

        return endpoints;
    }
}
