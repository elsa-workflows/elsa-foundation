using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace Elsa.Foundation.Identity.Core.Authorization;

public interface IPermissionCatalog
{
    IReadOnlyCollection<Permission> List();

    Permission? Find(string key);
}

/// <summary>
/// Additive contribution seam for the permission catalog. A feature that owns a host-control (or any
/// other) surface contributes its permissions through this interface instead of hard-coding them into
/// the identity domain (per ADR 0037). Contributors are aggregated by <c>CompositePermissionCatalog</c>
/// (<c>Elsa.Foundation.Identity</c>).
/// Register with <c>services.TryAddEnumerable(ServiceDescriptor.Singleton&lt;IPermissionContributor, MyContributor&gt;())</c>.
/// </summary>
public interface IPermissionContributor
{
    /// <summary>
    /// Gets the stable owner identifier for permissions supplied by this contributor.
    /// Existing contributors remain source-compatible; the implementation type is the
    /// deterministic fallback when a contributor does not provide an explicit owner.
    /// </summary>
    string OwnerId => GetType().FullName ?? GetType().Name;

    /// <summary>
    /// Gets the fully qualified implementation type used for provenance diagnostics.
    /// </summary>
    string ContributorType => GetType().FullName ?? GetType().Name;

    IEnumerable<Permission> Contribute();
}

public interface IPermissionEvaluator
{
    ValueTask<PermissionEvaluationResult> EvaluateAsync(PermissionEvaluationContext context, CancellationToken cancellationToken = default);
}

/// <summary>
/// Executes one canonical permission decision for request-internal callers that do not enter
/// ASP.NET Core's endpoint authorization middleware. Implementations apply the same normalized
/// principal validation, resource-handler precedence, catalog-backed evaluator, and cancellation
/// semantics as the endpoint policy handlers.
/// </summary>
[ReplacementContract]
public interface IPermissionAuthorizationService
{
    ValueTask<PermissionEvaluationResult> AuthorizeAsync(
        PermissionEvaluationContext context,
        CancellationToken cancellationToken = default);
}

public interface IPermissionResourceHandler
{
    ValueTask<PermissionEvaluationResult?> EvaluateAsync(PermissionEvaluationContext context, CancellationToken cancellationToken = default);
}

public interface IPermissionPolicyNameFormatter
{
    string Format(string permission);

    bool TryParse(string policyName, out string permission);
}

public interface IClaimsNormalizer
{
    ValueTask<ClaimsNormalizationResult> NormalizeAsync(ClaimsNormalizationContext context, CancellationToken cancellationToken = default);
}

public interface IClaimMappingRuleEvaluator
{
    bool Matches(ClaimsPrincipal principal, ClaimMappingRule rule);
}

[AttributeUsage(AttributeTargets.Interface, Inherited = false)]
public sealed class ReplacementContractAttribute : Attribute
{
}

public sealed record Permission(string Key, string DisplayName, string Category, string Description, IReadOnlySet<string>? Implies = null)
{
    /// <summary>
    /// Stable module or feature owner. These are intentionally non-positional so adding
    /// provenance does not break existing Permission constructors or deconstruction.
    /// </summary>
    public string OwnerId { get; init; } = string.Empty;

    /// <summary>
    /// Fully qualified contributor implementation type that supplied this definition.
    /// </summary>
    public string ContributorType { get; init; } = string.Empty;
}

public sealed record PermissionEvaluationContext(ClaimsPrincipal Principal, string Permission, string? TenantId = null, object? Resource = null)
{
    public CancellationToken CancellationToken { get; init; }
}

public sealed record PermissionEvaluationResult(bool Succeeded, string? Failure = null)
{
    public static PermissionEvaluationResult Success { get; } = new(true);

    public static PermissionEvaluationResult Denied(string? failure = null) => new(false, failure);
}

public sealed record ClaimMappingRule(
    string Id,
    string TenantId,
    string Provider,
    string MatchClaimType,
    string MatchValue,
    IReadOnlySet<string> GrantRoles,
    IReadOnlySet<string> GrantPermissions,
    int Order,
    bool StopOnMatch);

public sealed record ClaimsNormalizationContext(
    ClaimsPrincipal Principal,
    string TenantId,
    string Provider,
    IReadOnlyCollection<ClaimMappingRule> MappingRules,
    string AuthenticationType = "Elsa.Foundation.Identity");

public sealed record ClaimsNormalizationResult(ClaimsPrincipal Principal, IReadOnlySet<string> Roles, IReadOnlySet<string> Permissions);

public static class IdentityClaimTypes
{
    public const string Normalized = "elsa.identity.normalized";

    public const string TenantId = "elsa.identity.tenant_id";

    public const string Provider = "elsa.identity.provider";

    public const string Role = "elsa.identity.role";

    public const string Permission = "elsa.identity.permission";
}

public static class DefaultIdentityPermissionKeys
{
    public const string IdentityUsersRead = "identity.users.read";

    public const string IdentityUsersManage = "identity.users.manage";

    public const string IdentityRolesRead = "identity.roles.read";

    public const string IdentityRolesManage = "identity.roles.manage";

    public const string IdentityPermissionsRead = "identity.permissions.read";

    public const string IdentityApplicationsRead = "identity.applications.read";

    public const string IdentityApplicationsManage = "identity.applications.manage";

    public const string IdentityProvidersRead = "identity.providers.read";

    public const string IdentityProvidersManage = "identity.providers.manage";

    public const string IdentityCredentialsManage = "identity.credentials.manage";
}

public sealed record PermissionAuthorizationRequirement(string Permission) : IAuthorizationRequirement;

public sealed record PermissionSetAuthorizationRequirement : IAuthorizationRequirement
{
    public PermissionSetAuthorizationRequirement(PermissionRequirementMode mode, IReadOnlyList<string> permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);

        if (mode is not (PermissionRequirementMode.Any or PermissionRequirementMode.All))
            throw new ArgumentOutOfRangeException(nameof(mode));

        var canonicalPermissions = permissions
            .Select(PermissionKey.Normalize)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(permission => permission, StringComparer.Ordinal)
            .ToArray();
        if (canonicalPermissions.Length == 0)
            throw new ArgumentException("At least one permission is required.", nameof(permissions));

        Mode = mode;
        Permissions = Array.AsReadOnly(canonicalPermissions);
    }

    public PermissionRequirementMode Mode { get; }

    public IReadOnlyList<string> Permissions { get; }
}
