using Elsa.Agent.Api.Authorization;
using Elsa.Agent.Core.Contracts;
using Elsa.Foundation.Identity.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Agent.Api.Extensions;

public static class AgentApiServiceCollectionExtensions
{
    public static IServiceCollection AddFoundationAgentApi(this IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.Replace(ServiceDescriptor.Scoped<IAgentToolAuthorizer, HttpAgentToolAuthorizer>());
        services.AddPermissionContributor<AgentPermissionContributor>();
        return services;
    }
}
