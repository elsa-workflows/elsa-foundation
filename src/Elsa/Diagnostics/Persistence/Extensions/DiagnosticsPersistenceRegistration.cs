using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Elsa.Diagnostics.Persistence.Extensions;

/// <summary>Provides one explicit replacement path for a diagnostics store contract.</summary>
public static class DiagnosticsPersistenceRegistration
{
    /// <summary>
    /// Registers the fallback implementation only when no store has been selected. An explicit replacement
    /// registered before or after this call always owns the contract.
    /// </summary>
    public static IServiceCollection AddDefaultDiagnosticsStore<TContract, TImplementation>(
        this IServiceCollection services)
        where TContract : class
        where TImplementation : class, TContract
    {
        ArgumentNullException.ThrowIfNull(services);
        if (FindSelection<TContract>(services) is not null || services.Any(x => x.ServiceType == typeof(TContract)))
            return services;

        services.TryAddSingleton<TImplementation>();
        services.TryAddSingleton<TContract>(provider => provider.GetRequiredService<TImplementation>());
        services.AddSingleton(new DiagnosticsStoreSelection(typeof(TContract), typeof(TImplementation), IsExplicit: false));
        return services;
    }

    /// <summary>
    /// Selects the one explicit implementation for a replacement contract. A second explicit selection is a
    /// configuration conflict and is rejected immediately instead of silently becoming last-write-wins.
    /// </summary>
    public static IServiceCollection ReplaceDiagnosticsStore<TContract, TImplementation>(
        this IServiceCollection services)
        where TContract : class
        where TImplementation : class, TContract
    {
        ArgumentNullException.ThrowIfNull(services);
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
        services.AddSingleton<TImplementation>();
        services.AddSingleton<TContract>(provider => provider.GetRequiredService<TImplementation>());
        services.AddSingleton(new DiagnosticsStoreSelection(typeof(TContract), typeof(TImplementation), IsExplicit: true));
        return services;
    }

    private static DiagnosticsStoreSelection? FindSelection<TContract>(IServiceCollection services) =>
        services
            .Where(descriptor => descriptor.ServiceType == typeof(DiagnosticsStoreSelection))
            .Select(descriptor => (Descriptor: descriptor, Selection: descriptor.ImplementationInstance as DiagnosticsStoreSelection))
            .Where(item => item.Selection?.ContractType == typeof(TContract))
            .Select(item => item.Selection! with { Descriptor = item.Descriptor })
            .SingleOrDefault();

    private sealed record DiagnosticsStoreSelection(
        Type ContractType,
        Type ImplementationType,
        bool IsExplicit)
    {
        public ServiceDescriptor Descriptor { get; init; } = null!;
    }
}
