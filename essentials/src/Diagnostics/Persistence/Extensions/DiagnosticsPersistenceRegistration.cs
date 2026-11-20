using CShells.Lifecycle;
using Elsa.Diagnostics.Persistence.Draining;
using Elsa.Diagnostics.Persistence.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Elsa.Diagnostics.Persistence.Extensions;

/// <summary>Provides one explicit replacement path for a diagnostics store contract.</summary>
public static class DiagnosticsPersistenceRegistration
{
    /// <summary>
    /// Registers the fallback implementation only when no store has been selected. An explicit replacement
    /// registered before or after this call always owns the contract. Stores are scoped by default; select
    /// another lifetime explicitly only when the implementation's state and dependency graph justify it.
    /// </summary>
    public static IServiceCollection AddDefaultDiagnosticsStore<TContract, TImplementation>(
        this IServiceCollection services,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where TContract : class
        where TImplementation : class, TContract
    {
        ArgumentNullException.ThrowIfNull(services);
        ValidateLifetime(lifetime);
        if (FindSelection<TContract>(services) is not null || services.Any(x => x.ServiceType == typeof(TContract)))
            return services;

        services.TryAdd(ServiceDescriptor.Describe(typeof(TImplementation), typeof(TImplementation), lifetime));
        var contractDescriptor = ServiceDescriptor.Describe(
            typeof(TContract),
            provider => provider.GetRequiredService<TImplementation>(),
            lifetime);
        services.TryAdd(contractDescriptor);
        services.AddSingleton(new DiagnosticsStoreSelection(
            typeof(TContract),
            typeof(TImplementation),
            contractDescriptor,
            IsExplicit: false));
        return services;
    }

    /// <summary>
    /// Selects the one explicit implementation for a replacement contract. A second explicit selection is a
    /// configuration conflict and is rejected immediately instead of silently becoming last-write-wins.
    /// Stores are scoped by default; any other lifetime is an explicit host decision.
    /// </summary>
    public static IServiceCollection ReplaceDiagnosticsStore<TContract, TImplementation>(
        this IServiceCollection services,
        ServiceLifetime lifetime = ServiceLifetime.Scoped)
        where TContract : class
        where TImplementation : class, TContract
    {
        ArgumentNullException.ThrowIfNull(services);
        ValidateLifetime(lifetime);
        var existing = FindSelection<TContract>(services);
        if (existing is { IsExplicit: true })
        {
            throw new InvalidOperationException(
                $"Diagnostics replacement contract '{typeof(TContract).FullName}' already selects explicit implementation " +
                $"'{existing.ImplementationType.FullName}' and cannot also select '{typeof(TImplementation).FullName}'.");
        }

        services.RemoveAll<TContract>();
        if (existing is not null)
        {
            services.RemoveAll(existing.ImplementationType);
            services.Remove(existing.Descriptor);
        }
        services.RemoveAll<TImplementation>();
        services.Add(ServiceDescriptor.Describe(typeof(TImplementation), typeof(TImplementation), lifetime));
        var contractDescriptor = ServiceDescriptor.Describe(
            typeof(TContract),
            provider => provider.GetRequiredService<TImplementation>(),
            lifetime);
        services.Add(contractDescriptor);
        services.AddSingleton(new DiagnosticsStoreSelection(
            typeof(TContract),
            typeof(TImplementation),
            contractDescriptor,
            IsExplicit: true));
        return services;
    }

    /// <summary>
    /// Registers the singleton pull-only observer used by singleton live feeds. This lifetime is explicit:
    /// subscriber-loss totals must span all subscriptions and contain no scoped dependencies or payloads.
    /// </summary>
    public static IServiceCollection AddDiagnosticsPersistenceObservability(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        RejectObserverConflicts(services);
        services.AddDefaultDiagnosticsStore<IDiagnosticsPersistenceObserver, DiagnosticsPersistenceCounters>(
            ServiceLifetime.Singleton);
        services.TryAddSingleton<DiagnosticsSubscriberDeliveryLossBridge>();
        AddObserverRegistrationValidation(services);
        return services;
    }

    /// <summary>
    /// Contributes one already-registered singleton diagnostics drain to the host lifecycle. All contributed drains
    /// share one hosted coordinator, and repeated registration of the same concrete drain is idempotent.
    /// </summary>
    public static IServiceCollection AddDiagnosticsPersistenceLifecycle<TDrain>(this IServiceCollection services)
        where TDrain : class, IDiagnosticsPersistenceDrain
    {
        ArgumentNullException.ThrowIfNull(services);
        var concreteRegistrations = services
            .Where(descriptor => descriptor.ServiceType == typeof(TDrain))
            .ToArray();
        if (concreteRegistrations.Length != 1 ||
            concreteRegistrations[0].Lifetime != ServiceLifetime.Singleton)
        {
            throw new InvalidOperationException(
                $"Diagnostics drain '{typeof(TDrain).FullName}' must have exactly one singleton concrete registration " +
                $"before {nameof(AddDiagnosticsPersistenceLifecycle)} is called.");
        }

        if (!services.Any(descriptor =>
                descriptor.ServiceType == typeof(DiagnosticsPersistenceDrainRegistration) &&
                descriptor.ImplementationInstance is DiagnosticsPersistenceDrainRegistration registration &&
                registration.DrainType == typeof(TDrain)))
        {
            services.AddSingleton<IDiagnosticsPersistenceDrain>(
                provider => provider.GetRequiredService<TDrain>());
            if (typeof(IDiagnosticsPersistenceStartupResource).IsAssignableFrom(typeof(TDrain)))
            {
                services.AddSingleton<IDiagnosticsPersistenceStartupResource>(
                    provider => (IDiagnosticsPersistenceStartupResource)provider.GetRequiredService<TDrain>());
            }
            services.AddSingleton(new DiagnosticsPersistenceDrainRegistration(typeof(TDrain)));
        }

        if (!services.Any(descriptor =>
                descriptor.ServiceType == typeof(DiagnosticsPersistenceHostedServiceRegistration)))
        {
            services.TryAddSingleton<DiagnosticsPersistenceHostedService>();
            services.AddSingleton<IHostedService>(
                provider => provider.GetRequiredService<DiagnosticsPersistenceHostedService>());
            var initializerIndex = services.Count(descriptor =>
                descriptor.ServiceType == typeof(IShellInitializer));
            services.AddSingleton<IShellInitializer>(
                provider => provider.GetRequiredService<DiagnosticsPersistenceHostedService>());
            services.AddSingleton(new ShellInitializerRegistration(
                typeof(DiagnosticsPersistenceHostedService),
                LifecyclePhase.Default,
                0,
                initializerIndex,
                IsExplicit: true,
                $"{nameof(AddDiagnosticsPersistenceLifecycle)} (diagnostics drain start)"));
            var terminatorIndex = services.Count(descriptor =>
                descriptor.ServiceType == typeof(IShellTerminator));
            services.AddSingleton<IShellTerminator>(
                provider => provider.GetRequiredService<DiagnosticsPersistenceHostedService>());
            services.AddSingleton(new ShellTerminatorRegistration(
                typeof(DiagnosticsPersistenceHostedService),
                LifecyclePhase.Default,
                0,
                terminatorIndex,
                IsExplicit: true,
                $"{nameof(AddDiagnosticsPersistenceLifecycle)} (diagnostics drain stop)"));
            services.AddSingleton(new DiagnosticsPersistenceHostedServiceRegistration());
        }

        return services;
    }

    private static DiagnosticsStoreSelection? FindSelection<TContract>(IServiceCollection services) =>
        services
            .Where(descriptor => descriptor.ServiceType == typeof(DiagnosticsStoreSelection))
            .Select(descriptor => (Descriptor: descriptor, Selection: descriptor.ImplementationInstance as DiagnosticsStoreSelection))
            .Where(item => item.Selection?.ContractType == typeof(TContract))
            .Select(item => item.Selection! with { Descriptor = item.Descriptor })
            .SingleOrDefault();

    internal static (ServiceDescriptor ContractDescriptor, Type ImplementationType)? FindObserverSelection(
        IServiceCollection services)
    {
        var selection = FindSelection<IDiagnosticsPersistenceObserver>(services);
        return selection is null
            ? null
            : (selection.ContractDescriptor, selection.ImplementationType);
    }

    private static void ValidateLifetime(ServiceLifetime lifetime)
    {
        if (!Enum.IsDefined(lifetime))
            throw new ArgumentOutOfRangeException(nameof(lifetime), lifetime, "Unsupported diagnostics store lifetime.");
    }

    private static void RejectObserverConflicts(IServiceCollection services)
    {
        var result = new DiagnosticsPersistenceObserverRegistrationValidator(services).Validate();
        if (result.Failed)
            throw new InvalidOperationException(result.Failures.Single());
    }

    private static void AddObserverRegistrationValidation(IServiceCollection services)
    {
        if (services.Any(descriptor =>
                descriptor.ServiceType == typeof(DiagnosticsPersistenceObserverRegistrationValidator)))
            return;

        services.AddSingleton(new DiagnosticsPersistenceObserverRegistrationValidator(services));
        services.AddSingleton<
            IValidateOptions<DiagnosticsPersistenceObserverRegistrationOptions>,
            DiagnosticsPersistenceObserverRegistrationOptionsAdapter>();
        services.AddOptions<DiagnosticsPersistenceObserverRegistrationOptions>().ValidateOnStart();
    }

    private sealed record DiagnosticsStoreSelection(
        Type ContractType,
        Type ImplementationType,
        ServiceDescriptor ContractDescriptor,
        bool IsExplicit)
    {
        public ServiceDescriptor Descriptor { get; init; } = null!;
    }

    private sealed record DiagnosticsPersistenceDrainRegistration(Type DrainType);
    private sealed record DiagnosticsPersistenceHostedServiceRegistration;
}
