using Elsa.Foundation.Identity.Abstractions.Authorization;

namespace Elsa.Modularity.Api.Authorization;

/// <summary>
/// Permission keys owned by the module-management feature. Per ADR 0037, host-control permissions are
/// first-class identity permissions but their ownership stays with the feature that owns the host-control
/// surface, so they are contributed here rather than hard-coded into the identity domain.
/// </summary>
public static class ModuleManagementPermissionKeys
{
    public const string OwnerId = "elsa.modularity.module-management";

    public const string Read = "module-management.read";

    public const string Manage = "module-management.manage";
}

/// <summary>
/// Contributes the coarse module-management host-control permissions to the shared permission catalog.
/// </summary>
public sealed class ModuleManagementPermissionContributor : IPermissionContributor
{
    private const string Category = "Module management";

    public string OwnerId => ModuleManagementPermissionKeys.OwnerId;

    public IEnumerable<Permission> Contribute()
    {
        yield return new(
            ModuleManagementPermissionKeys.Read,
            "Read module management",
            Category,
            "Read the backend feature/module registry and management availability.");

        yield return new(
            ModuleManagementPermissionKeys.Manage,
            "Manage modules",
            Category,
            "Apply feature/module configuration changes on the backend host.",
            new HashSet<string> { ModuleManagementPermissionKeys.Read });
    }
}
