using Elsa.Activities.Http.Connections;
using Elsa.Activities.Http.Constants;
using Elsa.Connections.Extensions;
using Elsa.Connections.Http;
using Elsa.Connections.Workflows.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Connections.Workflows.Extensions;

public static class ConnectionsWorkflowsServiceCollectionExtensions
{
    public static IServiceCollection AddConnectionsWorkflows(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddConnections();
        services.Replace(ServiceDescriptor.Scoped<IHttpConnectionApplicator, HttpConnectionApplicator>());
        services.AddTransient<HttpConnectionRedirectHandler>();
        services.AddHttpClient(HttpActivityConstants.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
            {
                AllowAutoRedirect = false,
                MaxAutomaticRedirections = HttpConnectionRedirectHandler.DefaultMaxRedirects
            })
            .AddHttpMessageHandler<HttpConnectionRedirectHandler>();
        return services;
    }
}
