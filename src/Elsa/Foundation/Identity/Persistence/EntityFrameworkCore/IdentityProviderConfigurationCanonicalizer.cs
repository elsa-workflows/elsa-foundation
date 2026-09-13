namespace Elsa.Foundation.Identity.Persistence.EntityFrameworkCore;

internal static class IdentityProviderConfigurationCanonicalizer
{
    public const int MaximumIdentityLength = 400;

    public static string Normalize(string? value)
    {
        Validate(value, nameof(value));
        return IdentityEntityFrameworkKey.Normalize(value);
    }

    public static string TenantProviderId(string tenant, string provider) =>
        IdentityEntityFrameworkKey.TenantRecordId(Normalize(tenant), Normalize(provider));

    public static string GlobalProviderId(string provider) => IdentityEntityFrameworkKey.RecordId(Normalize(provider));

    public static bool Matches(string? tenant, string? tenantLookup, string provider, string providerLookup) =>
        (tenant is null ? tenantLookup is null : string.Equals(tenantLookup, Normalize(tenant), StringComparison.Ordinal)) &&
        string.Equals(providerLookup, Normalize(provider), StringComparison.Ordinal);

    public static void Validate(string? value, string parameterName)
    {
        if (value is null)
            return;
        if (value.Length > MaximumIdentityLength)
            throw new ArgumentException($"Identity key values cannot exceed {MaximumIdentityLength} UTF-16 code units.", parameterName);
    }
}
