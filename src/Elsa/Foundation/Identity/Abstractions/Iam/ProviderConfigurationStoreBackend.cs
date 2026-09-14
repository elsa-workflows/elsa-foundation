using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Foundation.Identity.Abstractions.Iam;

/// <summary>
/// Identifies the selected provider-configuration backend. Persistence features share this marker
/// so incompatible concrete selections fail deterministically instead of depending on registration
/// order.
/// </summary>
public sealed record ProviderConfigurationStoreBackend(
    string Name,
    ServiceDescriptor? ProviderConfigurationDescriptor = null,
    ServiceDescriptor? RevisionAwareProviderConfigurationDescriptor = null)
{
    public static void EnsureCompatible(string? existing, string incoming)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(incoming);
        if (existing is not null && !string.Equals(existing, incoming, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"IProviderConfigurationStore is already bound to '{existing}' and cannot also bind '{incoming}'. " +
                "Enable only one provider-configuration persistence feature.");
    }

    public void EnsureOwnsRegisteredContracts(IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        var providerDescriptors = services
            .Where(descriptor => descriptor.ServiceType == typeof(IProviderConfigurationStore))
            .ToArray();
        var revisionDescriptors = services
            .Where(descriptor => descriptor.ServiceType == typeof(IRevisionAwareProviderConfigurationStore))
            .ToArray();
        if (ProviderConfigurationDescriptor is null ||
            RevisionAwareProviderConfigurationDescriptor is null ||
            providerDescriptors.Length != 1 ||
            revisionDescriptors.Length != 1 ||
            !ReferenceEquals(providerDescriptors[0], ProviderConfigurationDescriptor) ||
            !ReferenceEquals(revisionDescriptors[0], RevisionAwareProviderConfigurationDescriptor))
        {
            throw new InvalidOperationException(
                $"Provider-configuration backend '{Name}' no longer exclusively owns both replacement contracts. " +
                "Remove conflicting host registrations or select only the intended backend.");
        }
    }
}
