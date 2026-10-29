using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core;
using Elsa.Agent.Handlers;
using Elsa.Agent.Services;
using Elsa.Events.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Agent.Extensions;

public static class AgentServiceCollectionExtensions
{
    public static IServiceCollection AddFoundationAgentAbstractions(this IServiceCollection services)
    {
        services.TryAddEventHandler<AgentContextCollecting, CollectAgentContext>();
        services.TryAddEventHandler<AgentCapabilitiesCollecting, CollectAgentCapabilities>();
        services.TryAddEventHandler<AgentToolsCollecting, CollectAgentTools>();
        services.TryAddScoped<IAgentPolicyEvaluator, DefaultAgentPolicyEvaluator>();
        services.TryAddScoped<IAgentContextSanitizer, DefaultAgentContextSanitizer>();
        services.TryAddScoped<IAgentContextCollector, DefaultAgentContextCollector>();
        services.TryAddScoped<IAgentCapabilityCatalog, DefaultAgentCapabilityCatalog>();
        services.TryAddSingleton<IAgentSessionService, InMemoryAgentSessionService>();
        // The process-wide store holds data only; current authority/tools are resolved by scoped facades.
        services.TryAddSingleton<InMemoryAgentProposalStore>();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<IAgentProposalService, InMemoryAgentProposalService>();
        services.TryAddScoped<IAgentActionProposalExecutor, DefaultAgentActionProposalExecutor>();
        services.TryAddScoped<IAgentToolAuthorizer, UnavailableAgentToolAuthorizer>();
        services.TryAddSingleton<InMemoryAgentAuditStore>();
        services.TryAddSingleton<IAgentAuditSink>(sp => sp.GetRequiredService<InMemoryAgentAuditStore>());
        services.TryAddSingleton<IAgentAuditReader>(sp => sp.GetRequiredService<InMemoryAgentAuditStore>());
        services.TryAddSingleton<IAgentFeedbackService, InMemoryAgentFeedbackService>();
        // Scoped so tools may depend on per-request services without being captured by a singleton.
        services.TryAddScoped<IAgentToolRegistry, DefaultAgentToolRegistry>();
        services.TryAddSingleton<IAgentTurnRegistry, AgentTurnRegistry>();
        services.TryAddSingleton<IAgentTurnStateStore, InMemoryAgentTurnStateStore>();
        services.AddOptions<AgentTurnOptions>();
        services.TryAddScoped<IAgentToolInvoker, DefaultAgentToolInvoker>();
        services.TryAddScoped<IAgentStreamingService, DefaultAgentTurnOrchestrator>();
        services.TryAddScoped<IAgentProviderRegistry, DefaultAgentProviderRegistry>();

        return services;
    }
}
