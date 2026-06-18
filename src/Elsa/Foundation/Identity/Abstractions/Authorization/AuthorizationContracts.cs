using System.Security.Claims;
using Elsa.Foundation.Identity.Abstractions.Iam;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace Elsa.Foundation.Identity.Abstractions.Authorization;

public interface IPermissionCatalog
{
    IReadOnlyCollection<Permission> List();

    Permission? Find(string key);
}

public interface IPermissionEvaluator
{
    ValueTask<PermissionEvaluationResult> EvaluateAsync(PermissionEvaluationContext context, CancellationToken cancellationToken = default);
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

public sealed record Permission(string Key, string DisplayName, string Category, string Description, IReadOnlySet<string>? Implies = null);

public sealed record PermissionEvaluationContext(ClaimsPrincipal Principal, string Permission, string? TenantId = null, object? Resource = null);

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

public sealed class DefaultIdentityPermissionCatalog : IPermissionCatalog
{
    private static readonly IReadOnlyCollection<Permission> Permissions =
    [
        new(DefaultIdentityPermissionKeys.IdentityUsersRead, "Read users", "Identity", "Read identity users."),
        new(DefaultIdentityPermissionKeys.IdentityUsersManage, "Manage users", "Identity", "Create, update, disable, and link identity users.", new HashSet<string> { DefaultIdentityPermissionKeys.IdentityUsersRead }),
        new(DefaultIdentityPermissionKeys.IdentityRolesRead, "Read roles", "Identity", "Read identity roles."),
        new(DefaultIdentityPermissionKeys.IdentityRolesManage, "Manage roles", "Identity", "Create, update, and assign identity roles.", new HashSet<string> { DefaultIdentityPermissionKeys.IdentityRolesRead, DefaultIdentityPermissionKeys.IdentityPermissionsRead }),
        new(DefaultIdentityPermissionKeys.IdentityPermissionsRead, "Read permissions", "Identity", "Read the shared identity permission catalog."),
        new(DefaultIdentityPermissionKeys.IdentityApplicationsRead, "Read applications", "Identity", "Read registered applications and clients."),
        new(DefaultIdentityPermissionKeys.IdentityApplicationsManage, "Manage applications", "Identity", "Create, update, and revoke applications and clients.", new HashSet<string> { DefaultIdentityPermissionKeys.IdentityApplicationsRead }),
        new(DefaultIdentityPermissionKeys.IdentityProvidersRead, "Read providers", "Identity", "Read configured authentication providers."),
        new(DefaultIdentityPermissionKeys.IdentityProvidersManage, "Manage providers", "Identity", "Create, update, test, and disable authentication providers.", new HashSet<string> { DefaultIdentityPermissionKeys.IdentityProvidersRead }),
        new(DefaultIdentityPermissionKeys.IdentityCredentialsManage, "Manage credentials", "Identity", "Issue, rotate, and revoke non-interactive credentials.")
    ];

    public IReadOnlyCollection<Permission> List() => Permissions;

    public Permission? Find(string key) => Permissions.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));
}

public sealed class ClaimsPermissionEvaluator(IPermissionCatalog catalog) : IPermissionEvaluator
{
    public ValueTask<PermissionEvaluationResult> EvaluateAsync(PermissionEvaluationContext context, CancellationToken cancellationToken = default)
    {
        var requested = Expand(context.Permission);
        var granted = context.Principal.Claims
            .Where(x => x.Type == IdentityClaimTypes.Permission)
            .Select(x => x.Value)
            .SelectMany(Expand)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return ValueTask.FromResult(requested.Any(granted.Contains)
            ? PermissionEvaluationResult.Success
            : PermissionEvaluationResult.Denied($"Missing permission '{context.Permission}'."));
    }

    private IEnumerable<string> Expand(string permission)
    {
        yield return permission;

        var catalogPermission = catalog.Find(permission);
        if (catalogPermission?.Implies is null)
            yield break;

        foreach (var implied in catalogPermission.Implies)
            yield return implied;
    }
}

public sealed class PermissionPolicyNameFormatter : IPermissionPolicyNameFormatter
{
    public const string Prefix = "Elsa.Permission:";

    public string Format(string permission) => $"{Prefix}{permission}";

    public bool TryParse(string policyName, out string permission)
    {
        if (policyName.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
        {
            permission = policyName[Prefix.Length..];
            return !string.IsNullOrWhiteSpace(permission);
        }

        permission = string.Empty;
        return false;
    }
}

public sealed class RequirePermissionAttribute : AuthorizeAttribute
{
    public RequirePermissionAttribute(string permission)
    {
        Permission = permission;
        Policy = PermissionPolicyNameFormatter.Prefix + permission;
    }

    public string Permission { get; }
}

public sealed record PermissionAuthorizationRequirement(string Permission) : IAuthorizationRequirement;

public sealed class RequirePermissionPolicyProvider(IOptions<AuthorizationOptions> options, IPermissionPolicyNameFormatter formatter) : DefaultAuthorizationPolicyProvider(options)
{
    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!formatter.TryParse(policyName, out var permission))
            return await base.GetPolicyAsync(policyName);

        return new AuthorizationPolicyBuilder()
            .AddRequirements(new PermissionAuthorizationRequirement(permission))
            .Build();
    }
}

public sealed class PermissionAuthorizationHandler(IPermissionEvaluator evaluator, IEnumerable<IPermissionResourceHandler> resourceHandlers)
    : AuthorizationHandler<PermissionAuthorizationRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionAuthorizationRequirement requirement)
    {
        var evaluationContext = new PermissionEvaluationContext(context.User, requirement.Permission, Resource: context.Resource);

        foreach (var resourceHandler in resourceHandlers)
        {
            var resourceResult = await resourceHandler.EvaluateAsync(evaluationContext);
            if (resourceResult is null)
                continue;

            if (resourceResult.Succeeded)
                context.Succeed(requirement);

            return;
        }

        var result = await evaluator.EvaluateAsync(evaluationContext);
        if (result.Succeeded)
            context.Succeed(requirement);
    }
}

public sealed class ClaimMappingRuleEvaluator : IClaimMappingRuleEvaluator
{
    public bool Matches(ClaimsPrincipal principal, ClaimMappingRule rule) =>
        principal.Claims.Any(x =>
            string.Equals(x.Type, rule.MatchClaimType, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(x.Value, rule.MatchValue, StringComparison.OrdinalIgnoreCase));
}

public sealed class DefaultClaimsNormalizer(IClaimMappingRuleEvaluator evaluator) : IClaimsNormalizer
{
    public ValueTask<ClaimsNormalizationResult> NormalizeAsync(ClaimsNormalizationContext context, CancellationToken cancellationToken = default)
    {
        var roles = context.Principal.Claims
            .Where(x => x.Type is ClaimTypes.Role or IdentityClaimTypes.Role)
            .Select(x => x.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var permissions = context.Principal.Claims
            .Where(x => x.Type == IdentityClaimTypes.Permission)
            .Select(x => x.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var rule in context.MappingRules.OrderBy(x => x.Order).ThenBy(x => x.Id, StringComparer.OrdinalIgnoreCase))
        {
            if (!evaluator.Matches(context.Principal, rule))
                continue;

            roles.UnionWith(rule.GrantRoles);
            permissions.UnionWith(rule.GrantPermissions);

            if (rule.StopOnMatch)
                break;
        }

        var identity = new ClaimsIdentity(context.Principal.Claims, context.AuthenticationType);
        identity.AddClaim(new Claim(IdentityClaimTypes.TenantId, context.TenantId));
        identity.AddClaim(new Claim(IdentityClaimTypes.Provider, context.Provider));

        foreach (var role in roles.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            identity.AddClaim(new Claim(IdentityClaimTypes.Role, role));

        foreach (var permission in permissions.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            identity.AddClaim(new Claim(IdentityClaimTypes.Permission, permission));

        return ValueTask.FromResult(new ClaimsNormalizationResult(new ClaimsPrincipal(identity), roles, permissions));
    }
}
