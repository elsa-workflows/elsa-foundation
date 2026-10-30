using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;

namespace Elsa.Studio.Tests;

internal static class BackendHttpClients
{
    public static IServiceCollection RouteAllBackendClientsThrough(this IServiceCollection services, HttpMessageHandler backend)
    {
        services.AddSingleton<IHttpMessageHandlerBuilderFilter>(new BackendFilter(backend));
        return services;
    }

    private sealed class BackendFilter(HttpMessageHandler backend) : IHttpMessageHandlerBuilderFilter
    {
        public Action<HttpMessageHandlerBuilder> Configure(Action<HttpMessageHandlerBuilder> next) => builder =>
        {
            next(builder);
            builder.PrimaryHandler = backend;
        };
    }
}
