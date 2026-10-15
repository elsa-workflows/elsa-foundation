using Elsa.Activities.Http.Connections;
using Elsa.Connections.Extensions;
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
        return services;
    }
}
