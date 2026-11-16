using ConsoleLogStreaming.AspNetCore.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;

namespace Elsa.Studio.ConsoleStream.Extensions;

public static class ConsoleStreamEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps the console-log-streaming endpoints (SignalR hub + HTTP routes). When <paramref name="authorizationPolicy"/>
    /// is supplied the endpoints are mapped inside an empty-prefix route group carrying that authorization policy, so
    /// every hub/HTTP route inherits the gate without altering the (absolute) paths configured on the host options.
    /// </summary>
    public static IEndpointRouteBuilder MapConsoleStreamStudio(this IEndpointRouteBuilder endpoints, string? authorizationPolicy = null)
    {
        if (string.IsNullOrWhiteSpace(authorizationPolicy))
        {
            endpoints.MapConsoleLogStreaming();
            return endpoints;
        }

        var group = endpoints.MapGroup("").RequireAuthorization(authorizationPolicy);
        group.MapConsoleLogStreaming();
        return endpoints;
    }

    /// <summary>
    /// Maps the console-log-streaming endpoints (SignalR hub + HTTP routes) only when the ConsoleStream
    /// feature is enabled in shell configuration. Call this on the application root endpoint builder so the
    /// hub is hosted on the root container rather than a recyclable shell scope. The authorization policy is
    /// required: this is the production wiring entry point, so it must never map the management-surface hub
    /// ungated by omission. A host that deliberately wants an ungated hub calls <see cref="MapConsoleStreamStudio"/>
    /// directly.
    /// </summary>
    public static IEndpointRouteBuilder MapConsoleStreamStudioIfEnabled(
        this IEndpointRouteBuilder endpoints,
        IConfiguration configuration,
        string authorizationPolicy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(authorizationPolicy);

        if (ConsoleStreamHookInstaller.IsFeatureEnabled(configuration))
            endpoints.MapConsoleStreamStudio(authorizationPolicy);

        return endpoints;
    }
}
