using Elsa.Diagnostics.Persistence.Observability;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
        services.TryAdd(ServiceDescriptor.Describe(
            typeof(TContract),
            provider => provider.GetRequiredService<TImplementation>(),
            lifetime));
        services.AddSingleton(new DiagnosticsStoreSelection(typeof(TContract), typeof(TImplementation), IsExplicit: false));
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
        services.Add(ServiceDescriptor.Describe(
            typeof(TContract),
            provider => provider.GetRequiredService<TImplementation>(),
            lifetime));
        services.AddSingleton(new DiagnosticsStoreSelection(typeof(TContract), typeof(TImplementation), IsExplicit: true));
        return services;
    }

    /// <summary>
    /// Registers the singleton pull-only observer used by singleton live feeds. This lifetime is explicit:
    /// subscriber-loss totals must span all subscriptions and contain no scoped dependencies or payloads.
    /// </summary>
    public static IServiceCollection AddDiagnosticsPersistenceObservability(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton<DiagnosticsPersistenceCounters>();
        services.TryAddSingleton<IDiagnosticsPersistenceObserver>(provider =>
            provider.GetRequiredService<DiagnosticsPersistenceCounters>());
        services.TryAddSingleton<DiagnosticsSubscriberDeliveryLossBridge>();
        return services;
    }

    private static DiagnosticsStoreSelection? FindSelection<TContract>(IServiceCollection services) =>
        services
            .Where(descriptor => descriptor.ServiceType == typeof(DiagnosticsStoreSelection))
            .Select(descriptor => (Descriptor: descriptor, Selection: descriptor.ImplementationInstance as DiagnosticsStoreSelection))
            .Where(item => item.Selection?.ContractType == typeof(TContract))
            .Select(item => item.Selection! with { Descriptor = item.Descriptor })
            .SingleOrDefault();

    private static void ValidateLifetime(ServiceLifetime lifetime)
    {
        if (!Enum.IsDefined(lifetime))
            throw new ArgumentOutOfRangeException(nameof(lifetime), lifetime, "Unsupported diagnostics store lifetime.");
    }

    private sealed record DiagnosticsStoreSelection(
        Type ContractType,
        Type ImplementationType,
        bool IsExplicit)
    {
        public ServiceDescriptor Descriptor { get; init; } = null!;
    }
}
