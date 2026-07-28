using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Persistence.Groundwork.DependencyInjection;

/// <summary>Guards the one-provider-per-host Groundwork composition invariant at registration time.</summary>
public static class GroundworkProviderSelectionRegistration
{
    /// <summary>
    /// Selects one concrete Groundwork provider leaf. Repeating the same provider is idempotent;
    /// selecting a different provider fails before either leaf can overwrite shared registrations.
    /// </summary>
    public static IServiceCollection SelectGroundworkProviderLeaf(
        this IServiceCollection services,
        string providerIdentity)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerIdentity);

        var selected = services
            .Where(descriptor => descriptor.ServiceType == typeof(GroundworkProviderLeafSelection))
            .Select(descriptor => descriptor.ImplementationInstance as GroundworkProviderLeafSelection)
            .Where(selection => selection is not null)
            .Select(selection => selection!.ProviderIdentity)
            .Append(providerIdentity)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (selected.Length > 1)
        {
            throw new InvalidOperationException(
                $"Conflicting Groundwork provider leaves were selected: {string.Join(", ", selected.Select(identity => $"'{identity}'"))}. " +
                "Select exactly one concrete Groundwork provider leaf per host.");
        }

        if (!services.Any(descriptor => descriptor.ServiceType == typeof(GroundworkProviderLeafSelection)))
            services.AddSingleton(new GroundworkProviderLeafSelection(providerIdentity));

        return services;
    }

    private sealed record GroundworkProviderLeafSelection(string ProviderIdentity);
}
