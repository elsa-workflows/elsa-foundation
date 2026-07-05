using Elsa.Api.FastEndpoints.Constants;

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
}
