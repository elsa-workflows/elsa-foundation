using Elsa.Activities.Http.Connections;
using Elsa.Activities.Http.Constants;
using Elsa.Activities.Http.Options;
using Elsa.Connections.Extensions;
using Elsa.Connections.Http;
using Elsa.Connections.Workflows.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Elsa.Connections.Workflows.Extensions;

public static class ConnectionsWorkflowsServiceCollectionExtensions
{
    public static IServiceCollection AddConnectionsWorkflows(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddConnections();
        services.Replace(ServiceDescriptor.Scoped<IHttpConnectionApplicator, HttpConnectionApplicator>());
        services.AddTransient(sp =>
        {
            var options = sp.GetService<IOptions<HttpActivityOptions>>()?.Value ?? new HttpActivityOptions();
            return new HttpConnectionRedirectHandler(options.AllowAutoRedirect, options.MaxAutomaticRedirections);
        });
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
