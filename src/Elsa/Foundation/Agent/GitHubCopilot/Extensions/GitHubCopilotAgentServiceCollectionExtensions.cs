using Elsa.Foundation.Agent.Abstractions.Contracts;
using Elsa.Foundation.Agent.GitHubCopilot.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Foundation.Agent.GitHubCopilot.Extensions;

public static class GitHubCopilotAgentServiceCollectionExtensions
{
    public static IServiceCollection AddGitHubCopilotAgentProvider(this IServiceCollection services)
    {
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IAgentProvider, GitHubCopilotAgentProvider>());
        return services;
    }
}
