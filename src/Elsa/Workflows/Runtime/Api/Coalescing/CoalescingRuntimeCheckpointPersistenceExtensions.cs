using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Core.Services.Coalescing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Workflows.Runtime.Api.Coalescing;

/// <summary>
/// Opt-in wiring for the burst-coalescing checkpoint persistence policy (E3-6, RT-10). Call this after the workflows
/// runtime services are registered to trade a bounded crash-replay window for Elsa-3-style single-write-per-burst
/// durability on straight-line segments. The default runtime keeps the Immediate policy, so this is a deliberate,
/// reversible selection; when it is not called every runtime store is a byte-for-byte pass-through as before.
/// </summary>
public static class CoalescingRuntimeCheckpointPersistenceExtensions
{
    /// <summary>
    /// Replaces the Immediate checkpoint persistence policy with the coalescing policy and decorates the runtime
    /// checkpoint/queue/outbox/state stores so intra-drain checkpoints fold into one atomic commit at quiescence.
    /// </summary>
    public static IServiceCollection AddCoalescingRuntimeCheckpointPersistence(
        this IServiceCollection services,
        Action<CoalescingRuntimeCheckpointPersistenceOptions>? configureOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (services.Any(descriptor => descriptor.ServiceType == typeof(CoalescingRegistrationMarker)))
            return services;

        var options = new CoalescingRuntimeCheckpointPersistenceOptions();
        configureOptions?.Invoke(options);

        var checkpointStoreDescriptor = RequirePreparedLedgerProvider(services);

        services.RemoveAll<CoalescingRuntimeCheckpointPersistenceOptions>();
        services.AddSingleton(options);

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IRuntimeCoalescingSessionAccessor, AsyncLocalRuntimeCoalescingSessionAccessor>();

        // Select the coalescing policy in place of the Immediate default.
        services.RemoveAll<IRuntimeCheckpointPersistencePolicy>();
        services.AddSingleton<IRuntimeCheckpointPersistencePolicy, CoalescingRuntimeCheckpointPersistencePolicy>();

        // Decorate the durable stores the coalescing session overlays. Each decorator passes through byte-for-byte when
        // no session is active; only an active session (established by the drain scope) redirects to the working set.
        services.DecorateWithCoalescing<IRuntimeCheckpointCommitStore, CoalescingRuntimeCheckpointCommitStore>();
        services.RemoveAll<IRuntimeCheckpointPreparedLedgerStore>();
        services.Add(new ServiceDescriptor(
            typeof(IRuntimeCheckpointPreparedLedgerStore),
            serviceProvider => (IRuntimeCheckpointPreparedLedgerStore)serviceProvider.GetRequiredService<IRuntimeCheckpointCommitStore>(),
            checkpointStoreDescriptor.Lifetime));
        services.DecorateWithCoalescing<IWorkflowSchedulerWorkQueue, CoalescingWorkflowSchedulerWorkQueue>();
        services.DecorateWithCoalescing<IRuntimePostCommitOutboxStore, CoalescingRuntimePostCommitOutboxStore>();
        services.RemoveAll<IPostCommitOutboxLookupStore>();
        services.AddScoped<IPostCommitOutboxLookupStore>(serviceProvider =>
            (IPostCommitOutboxLookupStore)serviceProvider.GetRequiredService<IRuntimePostCommitOutboxStore>());
        services.DecorateWithCoalescing<IWorkflowExecutionStateStore, CoalescingWorkflowExecutionStateStore>();
        services.DecorateWithCoalescing<IActivityExecutionStateStore, CoalescingActivityExecutionStateStore>();
        services.DecorateWithCoalescing<IDurableValueStateStore, CoalescingDurableValueStateStore>();
        services.DecorateWithCoalescing<ISchedulerStateStore, CoalescingSchedulerStateStore>();
        services.DecorateWithCoalescing<IActivityExecutionInspectionStore, CoalescingActivityExecutionInspectionStore>();

        // The drain scope factory presence is what makes the coordinator select its coalescing constructor.
        services.TryAddSingleton<IRuntimeCoalescingDrainScopeFactory, RuntimeCoalescingDrainScopeFactory>();
        services.AddSingleton<CoalescingRegistrationMarker>();

        return services;
    }

    private static ServiceDescriptor RequirePreparedLedgerProvider(IServiceCollection services)
    {
        var descriptor = services.LastOrDefault(candidate => candidate.ServiceType == typeof(IRuntimeCheckpointCommitStore))
            ?? throw new InvalidOperationException("Cannot enable checkpoint coalescing before a runtime checkpoint commit store is registered.");

        var supportsCapability = descriptor.ImplementationInstance is null ||
                                 descriptor.ImplementationInstance is IRuntimeCheckpointPreparedLedgerStore;
        if (descriptor.ImplementationType is { } implementationType)
            supportsCapability = typeof(IRuntimeCheckpointPreparedLedgerStore).IsAssignableFrom(implementationType);

        if (!supportsCapability)
        {
            throw new InvalidOperationException(
                $"{nameof(CheckpointPersistenceMode.Coalesced)} checkpoint persistence requires the selected durable provider to implement {nameof(IRuntimeCheckpointPreparedLedgerStore)}; no compatibility or sequential fallback is available.");
        }

        // Factory registrations are validated when the decorator is constructed because their produced type is not
        // knowable without activating the host. Instance/type registrations fail here during post-configuration.
        return descriptor;
    }

    // Captures the currently-registered (durable) implementation of TService as CoalescingInner<TService> and replaces
    // the TService registration with TDecorator. The decorator and the session reach the inner store via
    // CoalescingInner<TService> so resolving TService inside the decorator does not recurse.
    private static void DecorateWithCoalescing<TService, TDecorator>(this IServiceCollection services)
        where TService : class
        where TDecorator : class, TService
    {
        var descriptor = services.LastOrDefault(d => d.ServiceType == typeof(TService))
            ?? throw new InvalidOperationException(
                $"Cannot enable checkpoint coalescing: no registration found for '{typeof(TService)}'. Register the workflows runtime services before calling AddCoalescingRuntimeCheckpointPersistence.");

        services.Remove(descriptor);

        services.Add(new ServiceDescriptor(
            typeof(CoalescingInner<TService>),
            serviceProvider => new CoalescingInner<TService>((TService)InstantiateFromDescriptor(descriptor, serviceProvider)),
            descriptor.Lifetime));

        services.Add(new ServiceDescriptor(
            typeof(TService),
            serviceProvider => ActivatorUtilities.CreateInstance<TDecorator>(serviceProvider),
            descriptor.Lifetime));
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
