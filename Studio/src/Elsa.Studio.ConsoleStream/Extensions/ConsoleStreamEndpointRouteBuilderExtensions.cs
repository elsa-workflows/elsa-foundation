using ConsoleLogStreaming.AspNetCore.DependencyInjection;
using Microsoft.AspNetCore.Routing;

namespace Elsa.Studio.ConsoleStream.Extensions;

public static class ConsoleStreamEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapConsoleStreamStudio(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapConsoleLogStreaming();
        return endpoints;
    }
}
