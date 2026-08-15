using Elsa.Api.FastEndpoints.Constants;
using Elsa.Foundation.Identity.Abstractions.Authorization;

namespace Elsa.Api.FastEndpoints.Abstractions;

/// <summary>
/// The single owner of Elsa's endpoint permission composition: every endpoint accepts the wildcard
/// <see cref="PermissionNames.All"/> permission in addition to its own. The six endpoint base classes
/// delegate here (they derive from disjoint FastEndpoints bases whose <c>Permissions</c> method is
/// protected, so the call site cannot be shared — but the composition rule can), so a change to how
/// permissions compose lands in exactly one place (issue #414).
/// </summary>
public static class ElsaEndpointPermissions
{
    public static string[] Compose(string[] permissions) => [PermissionNames.All, .. permissions];

    /// <summary>
    /// Creates the one Foundation Identity policy used by an Elsa FastEndpoints base.
    /// </summary>
    /// <remarks>
    /// An endpoint without action permissions retains the historical wildcard requirement as a
    /// canonical single policy. Action-scoped endpoints retain the wildcard-plus-action OR
    /// behavior through one canonical any policy; passing separate policy names would make
    /// FastEndpoints compose them as AND.
    /// </remarks>
    public static string ComposePolicy(string[] permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);

        var codec = new PermissionPolicyCodec();
        return permissions.Length == 0
            ? codec.Format(PermissionPolicyDescriptor.Single(PermissionNames.All))
            : codec.Format(PermissionPolicyDescriptor.Any(Compose(permissions)));
    }
}
