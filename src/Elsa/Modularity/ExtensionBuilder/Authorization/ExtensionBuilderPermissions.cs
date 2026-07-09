using Elsa.Foundation.Identity.Abstractions.Authorization;

namespace Elsa.Modularity.ExtensionBuilder.Authorization;

/// <summary>
/// Permission keys owned by the Extension Builder feature. Per ADR 0037, host-control permissions are
/// first-class identity permissions but their ownership stays with the feature that owns the host-control
/// surface, so they are contributed here rather than hard-coded into the identity domain.
/// </summary>
public static class ExtensionBuilderPermissionKeys
{
    public const string Read = "extension-builder.read";

    public const string Manage = "extension-builder.manage";
}

/// <summary>
/// Contributes the coarse Extension Builder host-control permissions to the shared permission catalog.
/// </summary>
public sealed class ExtensionBuilderPermissionContributor : IPermissionContributor
{
    private const string Category = "Extension Builder";

    public IEnumerable<Permission> Contribute()
    {
        yield return new(
            ExtensionBuilderPermissionKeys.Read,
            "Read Extension Builder",
            Category,
            "Read Extension Builder capabilities, repositories, and build status on the backend host.");

        yield return new(
            ExtensionBuilderPermissionKeys.Manage,
            "Manage Extension Builder",
            Category,
            "Edit repositories, run builds, and promote or roll back extension packages on the backend host.",
            new HashSet<string> { ExtensionBuilderPermissionKeys.Read });
    }
}
