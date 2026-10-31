using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core;
using Elsa.Agent.Handlers;
using Elsa.Agent.Services;
using CShells.Lifecycle;
using Elsa.Events.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Agent.Extensions;

public static class AgentServiceCollectionExtensions
{
    public static IServiceCollection AddFoundationAgentAbstractions(this IServiceCollection services)
    {
        var registrationValidator = services.LastOrDefault(descriptor => descriptor.ServiceType == typeof(AgentRegistrationValidator))
            ?.ImplementationInstance as AgentRegistrationValidator;
        if (registrationValidator is null || !registrationValidator.Owns(services))
        {
            // Shells copy root registrations. Validate this collection, never its parent's metadata.
            registrationValidator = new AgentRegistrationValidator(services);
            services.Replace(ServiceDescriptor.Singleton(registrationValidator));
            foreach (var previous in services.Where(descriptor => descriptor.ServiceType == typeof(IShellInitializer)
                         && descriptor.ImplementationInstance is AgentRegistrationValidator).ToArray())
                services.Remove(previous);
        }
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IShellInitializer>(registrationValidator));
        services.TryAddEventHandler<AgentContextCollecting, CollectAgentContext>();
        services.TryAddEventHandler<AgentCapabilitiesCollecting, CollectAgentCapabilities>();
        services.TryAddEventHandler<AgentToolsCollecting, CollectAgentTools>();
        services.TryAddScoped<IAgentPolicyEvaluator, DefaultAgentPolicyEvaluator>();
        services.TryAddScoped<IAgentContextSanitizer, DefaultAgentContextSanitizer>();
        services.TryAddScoped<IAgentContextCollector>(AgentRegistrationValidator.Create<DefaultAgentContextCollector>);
        services.TryAddScoped<IAgentCapabilityCatalog, DefaultAgentCapabilityCatalog>();
        services.TryAddSingleton<IAgentSessionService, InMemoryAgentSessionService>();
        // The process-wide store holds data only; current authority/tools are resolved by scoped facades.
        services.TryAddSingleton<InMemoryAgentProposalStore>();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<AgentPreparedInvocationResolver>(AgentRegistrationValidator.Create<AgentPreparedInvocationResolver>);
        services.TryAddScoped<IAgentProposalService>(AgentRegistrationValidator.Create<InMemoryAgentProposalService>);
        services.TryAddScoped<IAgentActionProposalExecutor>(AgentRegistrationValidator.Create<DefaultAgentActionProposalExecutor>);
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
        services.AddOptions<AgentContextOptions>().Validate(options => options.SourceTimeout > TimeSpan.Zero
            && options.SourceTimeout <= TimeSpan.FromMilliseconds(uint.MaxValue - 1),
            "The context source timeout must be positive and within the supported finite timer range.");
        services.TryAddScoped<IAgentToolInvoker>(AgentRegistrationValidator.Create<DefaultAgentToolInvoker>);
        services.TryAddScoped<IAgentStreamingService>(AgentRegistrationValidator.Create<DefaultAgentTurnOrchestrator>);
        services.TryAddScoped<IAgentProviderRegistry, DefaultAgentProviderRegistry>();

        return services;
    }
}
