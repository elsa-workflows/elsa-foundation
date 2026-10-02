using Microsoft.AspNetCore.Authorization;

namespace Elsa.Studio.Api.Authorization;

/// <summary>
/// Requires the signed-in Studio user to hold at least one of <see cref="SatisfyingPermissions"/> (ADR 0037, #249).
/// The requirement is only the declaration; the Studio host registers the handler that decides it — allowing
/// anonymously when Studio auth is disabled (demo shell), otherwise requiring one of the keys as a
/// <see cref="StudioBridgeAuthorization.PermissionClaimType"/> claim. A host that registers no handler never satisfies
/// the requirement, so the endpoint fails closed.
/// </summary>
public sealed class StudioBridgePermissionRequirement : IAuthorizationRequirement
{
    public StudioBridgePermissionRequirement(params string[] satisfyingPermissions)
    {
        ArgumentNullException.ThrowIfNull(satisfyingPermissions);

        // An empty (or blank-keyed) list could only ever be satisfied by the auth-disabled posture, which would make a
        // mis-declared endpoint look fine in the demo shell and silently unreachable everywhere else. Refuse it loudly.
        if (satisfyingPermissions.Length == 0 || satisfyingPermissions.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("At least one non-empty permission key is required.", nameof(satisfyingPermissions));

        SatisfyingPermissions = [.. satisfyingPermissions];
    }

    /// <summary>The permission keys of which any one satisfies the requirement.</summary>
    public IReadOnlyList<string> SatisfyingPermissions { get; }
}
