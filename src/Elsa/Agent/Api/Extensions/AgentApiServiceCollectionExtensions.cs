using Elsa.Agent.Core.Extensions;
using Elsa.Agent.Api.Authorization;
using Elsa.Foundation.Identity.Abstractions.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Agent.Api.Extensions;

public static class AgentApiServiceCollectionExtensions
{
    public static IServiceCollection AddFoundationAgentApi(this IServiceCollection services)
    {
        services.AddFoundationAgentAbstractions();
        services.AddPermissionContributor<AgentPermissionContributor>();
        return services;
    }
}
