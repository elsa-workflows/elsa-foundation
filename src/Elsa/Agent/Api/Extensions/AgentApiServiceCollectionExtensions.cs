using Elsa.Agent.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Agent.Api.Extensions;

public static class AgentApiServiceCollectionExtensions
{
    public static IServiceCollection AddFoundationAgentApi(this IServiceCollection services)
    {
        services.AddFoundationAgentAbstractions();
        return services;
    }
}
