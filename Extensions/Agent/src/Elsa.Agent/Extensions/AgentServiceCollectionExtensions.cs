using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core;
using Elsa.Agent.Handlers;
using Elsa.Agent.Services;
using CShells.Lifecycle;
using Elsa.Events.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

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
        services.TryAddScoped<AgentEffectsAdmission>();
        services.AddOptions<AgentPrivilegePolicyOptions>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<AgentPrivilegePolicyOptions>, AgentPrivilegePolicyConfiguration>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IOptionsChangeTokenSource<AgentPrivilegePolicyOptions>, AgentPrivilegePolicyConfiguration>());
        services.TryAddScoped<AgentPrivilegePolicy>();
        services.TryAddEventHandler<AgentContextCollecting, CollectAgentContext>();
        services.TryAddEventHandler<AgentCapabilitiesCollecting, CollectAgentCapabilities>();
        services.TryAddEventHandler<AgentToolsCollecting, CollectAgentTools>();
        services.TryAddScoped<IAgentPolicyEvaluator, DefaultAgentPolicyEvaluator>();
        services.TryAddScoped<IAgentContextSanitizer, DefaultAgentContextSanitizer>();
        services.TryAddScoped<IAgentContextCollector>(AgentRegistrationValidator.Create<DefaultAgentContextCollector>);
        services.TryAddScoped<IAgentCapabilityCatalog, DefaultAgentCapabilityCatalog>();
        // Preview: the owner writes through the record store; at most one store is registered (validated).
        // A durable store is chosen with SelectAgentSessionRecordStore, which replaces only this default.
        AgentPersistenceSelection.TryAddDefaultSessionRecordStore(services, sp => new InMemoryAgentSessionRecordStore(sp.GetRequiredService<TimeProvider>()));
        services.TryAddSingleton<IAgentSessionService, InMemoryAgentSessionService>();
        // A graceful stop releases the owner's leases before the shell's container is disposed (story 2.1).
        if (!services.Any(descriptor => descriptor.ServiceType == typeof(ReleaseAgentSessionLeasesTerminator)))
            services.AddShellTerminator<ReleaseAgentSessionLeasesTerminator>(LifecyclePhase.Default, 0);
        // The process-wide store holds data only; current authority/tools are resolved by scoped facades.
        services.TryAddSingleton<InMemoryAgentProposalStore>();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<AgentPreparedInvocationResolver>(AgentRegistrationValidator.Create<AgentPreparedInvocationResolver>);
        services.TryAddScoped<IAgentProposalService>(AgentRegistrationValidator.Create<InMemoryAgentProposalService>);
        services.TryAddScoped<IAgentActionProposalExecutor>(AgentRegistrationValidator.Create<DefaultAgentActionProposalExecutor>);
        services.TryAddScoped<IAgentToolAuthorizer, UnavailableAgentToolAuthorizer>();
        services.TryAddSingleton<InMemoryAgentAuditStore>();
        // Preview: the sink, reader and ownership are one store's facets; the session owner associates ownership with
        // the same store. A durable store is chosen with SelectAgentAuditStore, which replaces the three together.
        AgentPersistenceSelection.TryAddDefaultAuditStore<InMemoryAgentAuditStore>(services);
        services.TryAddSingleton<IAgentFeedbackService, InMemoryAgentFeedbackService>();
        // Scoped so tools may depend on per-request services without being captured by a singleton.
        services.TryAddScoped<IAgentToolRegistry, DefaultAgentToolRegistry>();
        services.TryAddSingleton<IAgentTurnRegistry, AgentTurnRegistry>();
        // The default keeps the paused turn on the default owner's session record; any other session service gets a local map.
        services.TryAddSingleton<IAgentTurnStateStore>(sp => new InMemoryAgentTurnStateStore(sp.GetRequiredService<IAgentSessionService>() as InMemoryAgentSessionService));
        services.AddOptions<AgentTurnOptions>();
        services.AddOptions<AgentContextOptions>().Validate(options => options.SourceTimeout > TimeSpan.Zero
            && options.SourceTimeout <= TimeSpan.FromMilliseconds(uint.MaxValue - 1),
            "The context source timeout must be positive and within the supported finite timer range.");
        services.TryAddScoped<IAgentToolInvoker>(AgentRegistrationValidator.Create<DefaultAgentToolInvoker>);
        services.TryAddScoped<IAgentStreamingService>(AgentRegistrationValidator.Create<DefaultAgentTurnOrchestrator>);
        // Preview: scoped because participation is checked against the scoped lifecycle services.
        services.TryAddScoped<IAgentSessionPolicyService>(AgentRegistrationValidator.Create<DefaultAgentSessionPolicyService>);
        services.TryAddScoped<AgentDurableRecovery>();
        services.TryAddScoped<IAgentRecoverySupport, DefaultAgentRecoverySupport>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IShellInitializer, AgentDurableRecoveryValidator>());
        services.TryAddScoped<IAgentProviderRegistry, DefaultAgentProviderRegistry>();

        return services;
    }
}
