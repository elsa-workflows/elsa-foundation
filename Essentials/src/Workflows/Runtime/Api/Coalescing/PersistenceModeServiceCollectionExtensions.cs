using Elsa.Workflows.Runtime.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Coalescing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Runtime.Api.Coalescing;

/// <summary>
/// Registers the services that let a run use every persistence mode. Call this after the workflows runtime services
/// and their persistence provider are registered, because it wraps the stores that are registered at that point. A run
/// that resolves to Durable still takes the per-checkpoint path: without an active folding session every wrapped store
/// passes through unchanged. Every shell that composes the runtime API gets these services, so only a host that composes
/// the runtime directly can lack them, and such a host refuses to drain a run that needs them.
/// </summary>
public static class PersistenceModeServiceCollectionExtensions
{
    /// <summary>
    /// Replaces the per-checkpoint persistence policy with the folding policy and wraps the runtime
    /// checkpoint/queue/outbox/state stores so a Balanced run's checkpoints fold into one atomic commit at quiescence.
    /// Safe to call more than once: the stores are wrapped on the first call only.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">
    /// Sets the host's persistence-mode options, replacing any registered before. Without it the options already
    /// registered (for example by the WorkflowsRuntimeCheckpointPersistence feature) stay, and a host that registered
    /// none gets the defaults, so the order in which a shell's features add these services does not matter.
    /// </param>
    public static IServiceCollection AddPersistenceModes(
        this IServiceCollection services,
        Action<PersistenceModeOptions>? configureOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (configureOptions is not null)
        {
            var options = new PersistenceModeOptions();
            configureOptions(options);
            services.AddPersistenceModeOptions(options);
        }
        else
        {
            services.TryAddSingleton(new PersistenceModeOptions());
        }

        if (services.Any(descriptor => descriptor.ServiceType == typeof(CoalescingRegistrationMarker)))
            return services;

        var durableValueReuse = RuntimeCoalescingDurableValuePageReuseRegistration.CaptureBeforeDecoration(services);
        var decorations = new List<RuntimeCoalescingDurableValuePageReuseRegistration.ServiceDecoration>();

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IRuntimeCoalescingSessionAccessor, AsyncLocalRuntimeCoalescingSessionAccessor>();

        // Select the folding policy in place of the per-checkpoint default.
        services.RemoveAll<IRuntimeCheckpointPersistencePolicy>();
        services.AddSingleton<IRuntimeCheckpointPersistencePolicy, CoalescingRuntimeCheckpointPersistencePolicy>();

        // Decorate the durable stores the coalescing session overlays. Each decorator passes through byte-for-byte when
        // no session is active; only an active session (established by the drain scope) redirects to the working set.
        decorations.Add(services.DecorateWithCoalescing<IRuntimeCheckpointCommitStore, CoalescingRuntimeCheckpointCommitStore>());
        decorations.Add(services.DecorateWithCoalescing<IWorkflowSchedulerWorkQueue, CoalescingWorkflowSchedulerWorkQueue>());
        decorations.Add(services.DecorateWithCoalescing<IRuntimePostCommitOutboxStore, CoalescingRuntimePostCommitOutboxStore>());
        services.RemoveAll<IPostCommitOutboxLookupStore>();
        services.AddScoped<IPostCommitOutboxLookupStore>(serviceProvider =>
            (IPostCommitOutboxLookupStore)serviceProvider.GetRequiredService<IRuntimePostCommitOutboxStore>());
        decorations.Add(services.DecorateWithCoalescing<IWorkflowExecutionStateStore, CoalescingWorkflowExecutionStateStore>());
        decorations.Add(services.DecorateWithCoalescing<IActivityExecutionStateStore, CoalescingActivityExecutionStateStore>());
        decorations.Add(services.DecorateWithCoalescing<IDurableValueStateStore, CoalescingDurableValueStateStore>());
        decorations.Add(services.DecorateWithCoalescing<ISchedulerStateStore, CoalescingSchedulerStateStore>());
        decorations.Add(services.DecorateWithCoalescing<IActivityExecutionInspectionStore, CoalescingActivityExecutionInspectionStore>());

        // The drain scope factory is what lets the drain orchestrator fold: its DI factory resolves this with GetService,
        // and a run that resolves to Balanced on a host without it is refused rather than run Durable.
        services.TryAddScoped<IRuntimeCoalescingDrainScopeFactory, RuntimeCoalescingDrainScopeFactory>();
        var markerDescriptor = new ServiceDescriptor(
            typeof(CoalescingRegistrationMarker),
            typeof(CoalescingRegistrationMarker),
            ServiceLifetime.Singleton);
        services.Add(markerDescriptor);
        var reuseRegistrationDescriptor = ServiceDescriptor.Singleton(durableValueReuse);
        services.Add(reuseRegistrationDescriptor);
        durableValueReuse.CaptureAfterDecoration(decorations, markerDescriptor, reuseRegistrationDescriptor);

        return services;
    }

    /// <summary>
    /// Registers <paramref name="options"/> as the host's persistence-mode options, replacing any registered before. A
    /// mode that is not defined or a cap below one is refused here, so a misconfigured host fails when it is composed
    /// rather than when its first run drains.
    /// </summary>
    public static IServiceCollection AddPersistenceModeOptions(this IServiceCollection services, PersistenceModeOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        if (!Enum.IsDefined(options.Mode))
            throw new InvalidOperationException($"Persistence mode '{(int)options.Mode}' is not supported. Use one of: {string.Join(", ", Enum.GetNames<PersistenceMode>())}.");

        if (options.MaxActivitiesPerCommit <= 0)
            throw new InvalidOperationException($"{nameof(PersistenceModeOptions.MaxActivitiesPerCommit)} must be greater than zero.");

        services.RemoveAll<PersistenceModeOptions>();
        services.AddSingleton(options);
        return services;
    }

    // Captures the currently-registered (durable) implementation of TService as CoalescingInner<TService> and replaces
    // the TService registration with TDecorator. The decorator and the session reach the inner store via
    // CoalescingInner<TService> so resolving TService inside the decorator does not recurse.
    private static RuntimeCoalescingDurableValuePageReuseRegistration.ServiceDecoration DecorateWithCoalescing<TService, TDecorator>(this IServiceCollection services)
        where TService : class
        where TDecorator : class, TService
    {
        var descriptor = services.LastOrDefault(d => d.ServiceType == typeof(TService))
            ?? throw new InvalidOperationException(
                $"Cannot register the persistence modes: no registration found for '{typeof(TService)}'. Register the workflows runtime services before calling {nameof(AddPersistenceModes)}.");

        services.Remove(descriptor);

        var innerDescriptor = new ServiceDescriptor(
            typeof(CoalescingInner<TService>),
            serviceProvider => new CoalescingInner<TService>((TService)InstantiateFromDescriptor(descriptor, serviceProvider)),
            descriptor.Lifetime);
        services.Add(innerDescriptor);

        var wrapperDescriptor = new ServiceDescriptor(
            typeof(TService),
            serviceProvider => ActivatorUtilities.CreateInstance<TDecorator>(serviceProvider),
            descriptor.Lifetime);
        services.Add(wrapperDescriptor);
        return new(descriptor, innerDescriptor, wrapperDescriptor);
    }

    private static object InstantiateFromDescriptor(ServiceDescriptor descriptor, IServiceProvider serviceProvider)
    {
        if (descriptor.ImplementationInstance is { } instance)
            return instance;

        if (descriptor.ImplementationFactory is { } factory)
            return factory(serviceProvider);

        return ActivatorUtilities.CreateInstance(serviceProvider, descriptor.ImplementationType!);
    }

    private sealed class CoalescingRegistrationMarker;
}
