namespace Elsa.Foundation.Identity.Abstractions.Iam;

/// <summary>
/// Identifies the selected provider-configuration backend. Persistence features share this marker
/// so incompatible Groundwork and EF selections fail deterministically instead of depending on
/// registration order.
/// </summary>
public sealed record ProviderConfigurationStoreBackend(string Name)
{
    public static void EnsureCompatible(string? existing, string incoming)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(incoming);
        if (existing is not null && !string.Equals(existing, incoming, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"IProviderConfigurationStore is already bound to '{existing}' and cannot also bind '{incoming}'. " +
                "Enable only one provider-configuration persistence feature.");
    }
}
