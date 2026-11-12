using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core;
using Elsa.Weaver.Handlers;
using Elsa.Weaver.Services;
using CShells.Lifecycle;
using Elsa.Events.Core.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Elsa.Weaver.Extensions;

public static class WeaverServiceCollectionExtensions
{
    public static IServiceCollection AddWeaver(this IServiceCollection services)
    {
        var registrationValidator = services.LastOrDefault(descriptor => descriptor.ServiceType == typeof(WeaverRegistrationValidator))
            ?.ImplementationInstance as WeaverRegistrationValidator;
        if (registrationValidator is null || !registrationValidator.Owns(services))
        {
            // Shells copy root registrations. Validate this collection, never its parent's metadata.
            registrationValidator = new WeaverRegistrationValidator(services);
            services.Replace(ServiceDescriptor.Singleton(registrationValidator));
            foreach (var previous in services.Where(descriptor => descriptor.ServiceType == typeof(IShellInitializer)
                         && descriptor.ImplementationInstance is WeaverRegistrationValidator).ToArray())
                services.Remove(previous);
        }
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IShellInitializer>(registrationValidator));
        services.TryAddScoped<WeaverEffectsAdmission>();
        services.AddOptions<WeaverPrivilegePolicyOptions>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IConfigureOptions<WeaverPrivilegePolicyOptions>, WeaverPrivilegePolicyConfiguration>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IOptionsChangeTokenSource<WeaverPrivilegePolicyOptions>, WeaverPrivilegePolicyConfiguration>());
        services.TryAddScoped<WeaverPrivilegePolicy>();
        services.TryAddEventHandler<WeaverContextCollecting, CollectWeaverContext>();
        services.TryAddEventHandler<WeaverCapabilitiesCollecting, CollectWeaverCapabilities>();
        services.TryAddEventHandler<WeaverToolsCollecting, CollectWeaverTools>();
        services.TryAddScoped<IWeaverPolicyEvaluator, DefaultWeaverPolicyEvaluator>();
        services.TryAddScoped<IWeaverContextSanitizer, DefaultWeaverContextSanitizer>();
        services.TryAddScoped<IWeaverContextCollector>(WeaverRegistrationValidator.Create<DefaultWeaverContextCollector>);
        services.TryAddScoped<IWeaverCapabilityCatalog, DefaultWeaverCapabilityCatalog>();
        // Preview: the owner writes through the record store; at most one store is registered (validated).
        // A durable store is chosen with SelectWeaverSessionRecordStore, which replaces only this default.
        WeaverPersistenceSelection.TryAddDefaultSessionRecordStore(services, sp => new InMemoryWeaverSessionRecordStore(sp.GetRequiredService<TimeProvider>()));
        services.TryAddSingleton<IWeaverSessionService, InMemoryWeaverSessionService>();
        // A graceful stop releases the owner's leases before the shell's container is disposed (story 2.1).
        if (!services.Any(descriptor => descriptor.ServiceType == typeof(ReleaseWeaverSessionLeasesTerminator)))
            services.AddShellTerminator<ReleaseWeaverSessionLeasesTerminator>(LifecyclePhase.Default, 0);
        // The process-wide store holds data only; current authority/tools are resolved by scoped facades.
        services.TryAddSingleton<InMemoryWeaverProposalStore>();
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddScoped<WeaverPreparedInvocationResolver>(WeaverRegistrationValidator.Create<WeaverPreparedInvocationResolver>);
        services.TryAddScoped<IWeaverProposalService>(WeaverRegistrationValidator.Create<InMemoryWeaverProposalService>);
        services.TryAddScoped<IWeaverActionProposalExecutor>(WeaverRegistrationValidator.Create<DefaultWeaverActionProposalExecutor>);
        services.TryAddScoped<IWeaverToolAuthorizer, UnavailableWeaverToolAuthorizer>();
        services.TryAddSingleton<InMemoryWeaverAuditStore>();
        // Preview: the sink, reader and ownership are one store's facets; the session owner associates ownership with
        // the same store. A durable store is chosen with SelectWeaverAuditStore, which replaces the three together.
        WeaverPersistenceSelection.TryAddDefaultAuditStore<InMemoryWeaverAuditStore>(services);
        services.TryAddSingleton<IWeaverFeedbackService, InMemoryWeaverFeedbackService>();
        // Scoped so tools may depend on per-request services without being captured by a singleton.
        services.TryAddScoped<IWeaverToolRegistry, DefaultWeaverToolRegistry>();
        services.TryAddScoped<IWeaverSupportSource>(WeaverRegistrationValidator.Create<DefaultWeaverSupportSource>);
        services.TryAddSingleton<IWeaverTurnRegistry, WeaverTurnRegistry>();
        // The default keeps the paused turn on the default owner's session record; any other session service gets a local map.
        services.TryAddSingleton<IWeaverTurnStateStore>(sp => new InMemoryWeaverTurnStateStore(sp.GetRequiredService<IWeaverSessionService>() as InMemoryWeaverSessionService));
        services.AddOptions<WeaverTurnOptions>();
        services.AddOptions<WeaverContextOptions>().Validate(options => options.SourceTimeout > TimeSpan.Zero
            && options.SourceTimeout <= TimeSpan.FromMilliseconds(uint.MaxValue - 1),
            "The context source timeout must be positive and within the supported finite timer range.");
        services.TryAddScoped<IWeaverToolInvoker>(WeaverRegistrationValidator.Create<DefaultWeaverToolInvoker>);
        services.TryAddScoped<IWeaverStreamingService>(WeaverRegistrationValidator.Create<DefaultWeaverTurnOrchestrator>);
        // Preview: scoped because participation is checked against the scoped lifecycle services.
        services.TryAddScoped<IWeaverSessionPolicyService>(WeaverRegistrationValidator.Create<DefaultWeaverSessionPolicyService>);
        services.TryAddScoped<WeaverDurableRecovery>();
        services.TryAddScoped<IWeaverRecoverySupport, DefaultWeaverRecoverySupport>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IShellInitializer, WeaverDurableRecoveryValidator>());
        services.TryAddScoped<IWeaverProviderRegistry, DefaultWeaverProviderRegistry>();

        return services;
    }
}
