using Elsa.Foundation.Agent.Abstractions.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Foundation.Agent.Api.Extensions;

public static class AgentApiServiceCollectionExtensions
{
    public static IServiceCollection AddFoundationAgentApi(this IServiceCollection services)
    {
        services.AddFoundationAgentAbstractions();
        return services;
    }
}
