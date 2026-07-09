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

/// <summary>
/// Additive contribution seam for the permission catalog. A feature that owns a host-control (or any
/// other) surface contributes its permissions through this interface instead of hard-coding them into
/// the identity domain (per ADR 0037). Contributors are aggregated by <see cref="CompositePermissionCatalog"/>.
/// Register with <c>services.TryAddEnumerable(ServiceDescriptor.Singleton&lt;IPermissionContributor, MyContributor&gt;())</c>.
/// </summary>
public interface IPermissionContributor
{
    IEnumerable<Permission> Contribute();
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

public sealed class DefaultIdentityPermissionCatalog : IPermissionCatalog, IPermissionContributor
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

    IEnumerable<Permission> IPermissionContributor.Contribute() => Permissions;
}

/// <summary>
/// Aggregates every registered <see cref="IPermissionContributor"/> into a single permission catalog.
/// The default identity permissions are contributed by <see cref="DefaultIdentityPermissionCatalog"/>
/// and are protected: a later contributor whose key collides (case-insensitively) with an identity
/// permission is rejected at construction so feature contributions can never silently shadow or redefine
/// identity permissions. Duplicate keys between any two contributions are also rejected, so no
/// contribution can silently override another. Both conditions fail fast at construction (startup)
/// rather than producing an ambiguous catalog at request time.
/// </summary>
public sealed class CompositePermissionCatalog : IPermissionCatalog
{
    private readonly IReadOnlyDictionary<string, Permission> _byKey;
    private readonly IReadOnlyCollection<Permission> _permissions;

    public CompositePermissionCatalog(IEnumerable<IPermissionContributor> contributors)
    {
        var identityKeys = new DefaultIdentityPermissionCatalog()
            .List()
            .Select(x => x.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var byKey = new Dictionary<string, Permission>(StringComparer.OrdinalIgnoreCase);

        foreach (var contributor in contributors)
        {
            var isIdentity = contributor is DefaultIdentityPermissionCatalog;

            foreach (var permission in contributor.Contribute())
            {
                if (!isIdentity && identityKeys.Contains(permission.Key))
                    throw new InvalidOperationException(
                        $"Permission contributor '{contributor.GetType().FullName}' attempted to contribute permission '{permission.Key}', which is a reserved identity permission and cannot be shadowed.");

                if (byKey.TryGetValue(permission.Key, out var existing))
                    throw new InvalidOperationException(
                        $"Permission contributor '{contributor.GetType().FullName}' contributed duplicate permission key '{permission.Key}' (already contributed as '{existing.DisplayName}').");

                byKey.Add(permission.Key, permission);
            }
        }

        _byKey = byKey;
        _permissions = byKey.Values.ToArray();
    }

    public IReadOnlyCollection<Permission> List() => _permissions;

    public Permission? Find(string key) => _byKey.GetValueOrDefault(key);
}

public sealed class ClaimsPermissionEvaluator(IPermissionCatalog catalog) : IPermissionEvaluator
{
    public ValueTask<PermissionEvaluationResult> EvaluateAsync(PermissionEvaluationContext context, CancellationToken cancellationToken = default)
    {
        var granted = context.Principal.Claims
            .Where(x => x.Type == IdentityClaimTypes.Permission)
            .Select(x => x.Value)
            .SelectMany(ExpandGranted)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return ValueTask.FromResult(granted.Contains(context.Permission)
            ? PermissionEvaluationResult.Success
            : PermissionEvaluationResult.Denied($"Missing permission '{context.Permission}'."));
    }

    private IEnumerable<string> ExpandGranted(string permission)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var stack = new Stack<string>();
        stack.Push(permission);

        while (stack.TryPop(out var current))
        {
            if (!visited.Add(current))
                continue;

            yield return current;

            var catalogPermission = catalog.Find(current);
            if (catalogPermission?.Implies is null)
                continue;

            foreach (var implied in catalogPermission.Implies)
                stack.Push(implied);
        }
    }
}

public sealed record AuthorizationPolicyProviderFallback(IAuthorizationPolicyProvider Provider);

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

public sealed class RequirePermissionPolicyProvider(
    IOptions<AuthorizationOptions> options,
    IPermissionPolicyNameFormatter formatter,
    AuthorizationPolicyProviderFallback? fallback = null) : IAuthorizationPolicyProvider
{
    private readonly DefaultAuthorizationPolicyProvider _defaultProvider = new(options);

    public async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!formatter.TryParse(policyName, out var permission))
            return fallback is not null ? await fallback.Provider.GetPolicyAsync(policyName) : await _defaultProvider.GetPolicyAsync(policyName);

        return new AuthorizationPolicyBuilder()
            .AddRequirements(new PermissionAuthorizationRequirement(permission))
            .Build();
    }

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() =>
        fallback?.Provider.GetDefaultPolicyAsync() ?? _defaultProvider.GetDefaultPolicyAsync();

    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() =>
        fallback?.Provider.GetFallbackPolicyAsync() ?? _defaultProvider.GetFallbackPolicyAsync();
}

public sealed class PermissionAuthorizationHandler(IPermissionEvaluator evaluator, IEnumerable<IPermissionResourceHandler> resourceHandlers)
    : AuthorizationHandler<PermissionAuthorizationRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionAuthorizationRequirement requirement)
    {
        var tenantId = ResolveTenantId(context);
        var evaluationContext = new PermissionEvaluationContext(context.User, requirement.Permission, tenantId, context.Resource);
        var resourceDenied = false;

        foreach (var resourceHandler in resourceHandlers)
        {
            var resourceResult = await resourceHandler.EvaluateAsync(evaluationContext);
            if (resourceResult is null)
                continue;

            if (resourceResult.Succeeded)
            {
                context.Succeed(requirement);
                return;
            }

            resourceDenied = true;
        }

        var result = await evaluator.EvaluateAsync(evaluationContext);
        if (result.Succeeded)
            context.Succeed(requirement);
        else if (resourceDenied)
            context.Fail();
    }

    private static string? ResolveTenantId(AuthorizationHandlerContext context)
    {
        if (context.Resource is not null)
        {
            var property = context.Resource.GetType().GetProperty("TenantId");
            if (property?.GetValue(context.Resource) is string resourceTenantId && !string.IsNullOrWhiteSpace(resourceTenantId))
                return resourceTenantId;
        }

        return context.User.FindFirst(IdentityClaimTypes.TenantId)?.Value;
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
            .Where(x => x.Type == ClaimTypes.Role)
            .Select(x => x.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var effectiveRules = context.MappingRules
            .Where(x => string.Equals(x.TenantId, context.TenantId, StringComparison.OrdinalIgnoreCase))
            .Where(x => string.Equals(x.Provider, context.Provider, StringComparison.OrdinalIgnoreCase));

        foreach (var rule in effectiveRules.OrderBy(x => x.Order).ThenBy(x => x.Id, StringComparer.OrdinalIgnoreCase))
        {
            if (!evaluator.Matches(context.Principal, rule))
                continue;

            roles.UnionWith(rule.GrantRoles);
            permissions.UnionWith(rule.GrantPermissions);

            if (rule.StopOnMatch)
                break;
        }

        var identity = new ClaimsIdentity(
            context.Principal.Claims.Where(x => !IsInternalIdentityClaim(x.Type)),
            context.AuthenticationType);
        identity.AddClaim(new Claim(IdentityClaimTypes.TenantId, context.TenantId));
        identity.AddClaim(new Claim(IdentityClaimTypes.Provider, context.Provider));

        foreach (var role in roles.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            identity.AddClaim(new Claim(IdentityClaimTypes.Role, role));

        foreach (var permission in permissions.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            identity.AddClaim(new Claim(IdentityClaimTypes.Permission, permission));

        return ValueTask.FromResult(new ClaimsNormalizationResult(new ClaimsPrincipal(identity), roles, permissions));
    }

    private static bool IsInternalIdentityClaim(string claimType) =>
        claimType is IdentityClaimTypes.TenantId or IdentityClaimTypes.Provider or IdentityClaimTypes.Role or IdentityClaimTypes.Permission;
}
