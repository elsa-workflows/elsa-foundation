using Elsa.Foundation.Identity.Abstractions.Ownership;

namespace Elsa.Foundation.Identity.Abstractions;

public sealed class FoundationIdentityOptions
{
    public OwnershipMode OwnershipMode { get; set; } = OwnershipMode.FoundationOwned;

    public ProviderCapabilities ProviderCapabilities { get; set; } = ProviderCapabilities.FoundationReference;

    public PermissionPropagationMode PermissionPropagation { get; set; } = PermissionPropagationMode.ImmediateServerSide;

    public bool IsDevelopmentOrDemo { get; set; }

    public string? SigningKey { get; set; }

    public bool RequireHttpsMetadata { get; set; } = true;

    public bool RequireUniqueEmail { get; set; }

    public IReadOnlySet<string> AllowedSecretHashAlgorithms { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        SecretHashAlgorithms.Pbkdf2Sha256
    };
}

public static class SecretHashAlgorithms
{
    public const string Pbkdf2Sha256 = "PBKDF2-SHA256";
}
