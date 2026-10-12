using Elsa.Foundation.Identity.Core.Ownership;

namespace Elsa.Foundation.Identity;

public sealed class FoundationIdentityOptions
{
    public IReadOnlySet<string> NormalizedAuthenticationTypes { get; set; } = new HashSet<string>(StringComparer.Ordinal);

    public OwnershipMode OwnershipMode { get; set; } = OwnershipMode.FoundationOwned;

    public ProviderCapabilities ProviderCapabilities { get; set; } = ProviderCapabilities.FoundationReference;

    public PermissionPropagationMode PermissionPropagation { get; set; } = PermissionPropagationMode.ImmediateServerSide;

    public string? SigningKey { get; set; }

    public bool RequireUniqueEmail { get; set; }
}
