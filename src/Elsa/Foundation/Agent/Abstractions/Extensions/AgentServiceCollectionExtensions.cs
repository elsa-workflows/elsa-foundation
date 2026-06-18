using Elsa.Foundation.Agent.Abstractions.Contracts;
using Elsa.Foundation.Agent.Abstractions.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Foundation.Agent.Abstractions.Extensions;

public static class AgentServiceCollectionExtensions
{
    public static IServiceCollection AddFoundationAgentAbstractions(this IServiceCollection services)
    {
        services.TryAddScoped<IAgentPolicyEvaluator, DefaultAgentPolicyEvaluator>();
        services.TryAddScoped<IAgentContextSanitizer, DefaultAgentContextSanitizer>();
        services.TryAddScoped<IAgentContextCollector, DefaultAgentContextCollector>();
        services.TryAddScoped<IAgentCapabilityCatalog, DefaultAgentCapabilityCatalog>();
        services.TryAddSingleton<IAgentSessionService, InMemoryAgentSessionService>();
        services.TryAddSingleton<IAgentProposalService, InMemoryAgentProposalService>();
        services.TryAddSingleton<IAgentActionProposalExecutor, NoopAgentActionProposalExecutor>();
        services.TryAddSingleton<InMemoryAgentAuditStore>();
        services.TryAddSingleton<IAgentAuditSink>(sp => sp.GetRequiredService<InMemoryAgentAuditStore>());
        services.TryAddSingleton<IAgentAuditReader>(sp => sp.GetRequiredService<InMemoryAgentAuditStore>());
        services.TryAddSingleton<IAgentFeedbackService, InMemoryAgentFeedbackService>();
        services.TryAddScoped<IAgentStreamingService, DefaultAgentStreamingService>();
        services.TryAddScoped<IAgentProviderRegistry, DefaultAgentProviderRegistry>();

        return services;
    }
}
