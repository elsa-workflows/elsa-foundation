using System.Collections.Frozen;
using System.Security.Claims;
using Elsa.Foundation.Identity.Abstractions.Iam;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
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

public sealed class DefaultIdentityPermissionCatalog : IPermissionCatalog, IPermissionContributor
{
    public const string DefaultOwnerId = "elsa.foundation.identity";

    public string OwnerId => DefaultOwnerId;

    public string ContributorType => typeof(DefaultIdentityPermissionCatalog).FullName!;

    private static readonly IReadOnlyCollection<Permission> Permissions = Array.AsReadOnly<Permission>(
    [
        Owned(DefaultIdentityPermissionKeys.IdentityUsersRead, "Read users", "Identity", "Read identity users."),
        Owned(DefaultIdentityPermissionKeys.IdentityUsersManage, "Manage users", "Identity", "Create, update, disable, and link identity users.", new HashSet<string> { DefaultIdentityPermissionKeys.IdentityUsersRead }),
        Owned(DefaultIdentityPermissionKeys.IdentityRolesRead, "Read roles", "Identity", "Read identity roles."),
        Owned(DefaultIdentityPermissionKeys.IdentityRolesManage, "Manage roles", "Identity", "Create, update, and assign identity roles.", new HashSet<string> { DefaultIdentityPermissionKeys.IdentityRolesRead, DefaultIdentityPermissionKeys.IdentityPermissionsRead }),
        Owned(DefaultIdentityPermissionKeys.IdentityPermissionsRead, "Read permissions", "Identity", "Read the shared identity permission catalog."),
        Owned(DefaultIdentityPermissionKeys.IdentityApplicationsRead, "Read applications", "Identity", "Read registered applications and clients."),
        Owned(DefaultIdentityPermissionKeys.IdentityApplicationsManage, "Manage applications", "Identity", "Create, update, and revoke applications and clients.", new HashSet<string> { DefaultIdentityPermissionKeys.IdentityApplicationsRead }),
        Owned(DefaultIdentityPermissionKeys.IdentityProvidersRead, "Read providers", "Identity", "Read configured authentication providers."),
        Owned(DefaultIdentityPermissionKeys.IdentityProvidersManage, "Manage providers", "Identity", "Create, update, test, and disable authentication providers.", new HashSet<string> { DefaultIdentityPermissionKeys.IdentityProvidersRead }),
        Owned(DefaultIdentityPermissionKeys.IdentityCredentialsManage, "Manage credentials", "Identity", "Issue, rotate, and revoke non-interactive credentials.")
    ]);

    private static Permission Owned(string key, string displayName, string category, string description, IReadOnlySet<string>? implies = null) =>
        new(key, displayName, category, description, implies?.ToFrozenSet(StringComparer.Ordinal))
        {
            OwnerId = DefaultOwnerId,
            ContributorType = typeof(DefaultIdentityPermissionCatalog).FullName!
        };

    public IReadOnlyCollection<Permission> List() => Permissions;

    public Permission? Find(string key)
    {
        try
        {
            var canonicalKey = PermissionKey.Normalize(key);
            return Permissions.FirstOrDefault(permission =>
                string.Equals(PermissionKey.Normalize(permission.Key), canonicalKey, StringComparison.Ordinal));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

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
        ArgumentNullException.ThrowIfNull(contributors);

        var byKey = new Dictionary<string, Permission>(StringComparer.Ordinal);

        foreach (var contributor in contributors)
        {
            ArgumentNullException.ThrowIfNull(contributor);

            var ownerId = string.IsNullOrWhiteSpace(contributor.OwnerId)
                ? contributor.GetType().FullName ?? contributor.GetType().Name
                : contributor.OwnerId;
            var contributorType = string.IsNullOrWhiteSpace(contributor.ContributorType)
                ? contributor.GetType().FullName ?? contributor.GetType().Name
                : contributor.ContributorType;

            foreach (var permission in contributor.Contribute())
            {
                ArgumentNullException.ThrowIfNull(permission);

                var canonicalKey = CanonicalizeCatalogKey(permission.Key, "key", ownerId, contributorType);
                if (canonicalKey == PermissionKey.Wildcard)
                    throw new InvalidOperationException(
                        $"Permission contributor '{contributorType}' owned by '{ownerId}' cannot catalog reserved wildcard permission '{permission.Key}'.");

                if (permission.Implies is not null)
                {
                    foreach (var implied in permission.Implies)
                    {
                        var canonicalImplied = CanonicalizeCatalogKey(implied, "implication target", ownerId, contributorType);
                        if (canonicalImplied == PermissionKey.Wildcard)
                            throw new InvalidOperationException(
                                $"Permission '{permission.Key}' contributed by '{contributorType}' owned by '{ownerId}' cannot imply reserved wildcard permission '{implied}'.");
                    }
                }

                var ownedPermission = permission with
                {
                    OwnerId = ownerId,
                    ContributorType = contributorType,
                    Implies = permission.Implies?.ToFrozenSet(StringComparer.Ordinal)
                };

                if (byKey.TryGetValue(canonicalKey, out var existing))
                {
                    var reservedIdentity = string.Equals(existing.OwnerId, DefaultIdentityPermissionCatalog.DefaultOwnerId, StringComparison.Ordinal)
                        ? " This is a reserved identity permission and cannot be shadowed."
                        : string.Empty;
                    throw new InvalidOperationException(
                        $"Duplicate canonical permission key '{canonicalKey}' (declared as '{permission.Key}'; existing declaration '{existing.Key}'): existing owner '{existing.OwnerId}', contributor type '{existing.ContributorType}'; duplicate owner '{ownedPermission.OwnerId}', contributor type '{ownedPermission.ContributorType}'.{reservedIdentity}");
                }

                byKey.Add(canonicalKey, ownedPermission);
            }
        }

        _byKey = byKey.ToFrozenDictionary(StringComparer.Ordinal);
        _permissions = Array.AsReadOnly(_byKey
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => pair.Value)
            .ToArray());
    }

    public IReadOnlyCollection<Permission> List() => _permissions;

    public Permission? Find(string key)
    {
        if (string.IsNullOrEmpty(key))
            return null;

        try
        {
            return _byKey.GetValueOrDefault(PermissionKey.Normalize(key));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static string CanonicalizeCatalogKey(string key, string kind, string ownerId, string contributorType)
    {
        try
        {
            return PermissionKey.Normalize(key);
        }
        catch (ArgumentException exception)
        {
            throw new InvalidOperationException(
                $"Permission contributor '{contributorType}' owned by '{ownerId}' supplied malformed {kind} '{key}'.",
                exception);
        }
    }
}

public sealed class ClaimsPermissionEvaluator(IPermissionCatalog catalog) : IPermissionEvaluator
{
    public ValueTask<PermissionEvaluationResult> EvaluateAsync(PermissionEvaluationContext context, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        context.CancellationToken.ThrowIfCancellationRequested();

        var permissions = BuildCatalogIndex();
        var granted = context.Principal.Claims
            .Where(x => x.Type == IdentityClaimTypes.Permission)
            .Select(x => x.Value)
            .Select(CanonicalizeClaim)
            .Where(x => x is not null)
            .Select(x => x!)
            .SelectMany(permission => ExpandGranted(permission, permissions, cancellationToken, context.CancellationToken))
            .ToHashSet(StringComparer.Ordinal);

        var requested = CanonicalizeRequested(context.Permission);
        var succeeds = requested.Length > 0 &&
            (granted.Contains(requested) || (requested != PermissionKey.Wildcard && granted.Contains(PermissionKey.Wildcard)));

        return ValueTask.FromResult(succeeds
            ? PermissionEvaluationResult.Success
            : PermissionEvaluationResult.Denied($"Missing permission '{context.Permission}'."));
    }

    private IReadOnlyDictionary<string, Permission> BuildCatalogIndex()
    {
        var byKey = new Dictionary<string, Permission>(StringComparer.Ordinal);

        foreach (var permission in catalog.List())
        {
            string key;
            try
            {
                key = CanonicalizeCatalogKey(permission.Key);
            }
            catch (ArgumentException exception)
            {
                throw new InvalidOperationException($"Permission catalog contains malformed key '{permission.Key}'.", exception);
            }

            if (key == PermissionKey.Wildcard)
                throw new InvalidOperationException("The wildcard permission cannot be cataloged.");

            if (!byKey.TryAdd(key, permission))
                throw new InvalidOperationException($"Permission catalog contains duplicate canonical key '{key}'.");

            if (permission.Implies is null)
                continue;

            foreach (var implied in permission.Implies)
            {
                string canonicalImplied;
                try
                {
                    canonicalImplied = CanonicalizeCatalogKey(implied);
                }
                catch (ArgumentException exception)
                {
                    throw new InvalidOperationException($"Permission '{permission.Key}' contains malformed implication '{implied}'.", exception);
                }

                if (canonicalImplied == PermissionKey.Wildcard)
                    throw new InvalidOperationException($"Permission '{permission.Key}' cannot imply the wildcard permission.");
            }
        }

        return byKey;
    }

    private static IEnumerable<string> ExpandGranted(
        string permission,
        IReadOnlyDictionary<string, Permission> permissions,
        CancellationToken cancellationToken,
        CancellationToken contextCancellationToken)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<string>();
        stack.Push(permission);

        while (stack.TryPop(out var current))
        {
            cancellationToken.ThrowIfCancellationRequested();
            contextCancellationToken.ThrowIfCancellationRequested();

            if (!visited.Add(current))
                continue;

            yield return current;

            if (current == PermissionKey.Wildcard)
                continue;

            if (!permissions.TryGetValue(current, out var catalogPermission))
                continue;

            if (catalogPermission.Implies is null)
                continue;

            foreach (var implied in catalogPermission.Implies)
                stack.Push(CanonicalizeCatalogKey(implied));
        }
    }

    private static string? CanonicalizeClaim(string? permission)
    {
        if (string.IsNullOrWhiteSpace(permission))
            return null;

        try
        {
            return PermissionKey.Normalize(permission);
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    private static string CanonicalizeRequested(string? permission) =>
        string.IsNullOrWhiteSpace(permission) ? string.Empty : PermissionKey.Normalize(permission);

    private static string CanonicalizeCatalogKey(string permission) => PermissionKey.Normalize(permission);

}

public sealed record AuthorizationPolicyProviderFallback(IAuthorizationPolicyProvider Provider);

public sealed class PermissionPolicyNameFormatter : IPermissionPolicyNameFormatter
{
    public const string Prefix = PermissionPolicyCodec.Prefix;

    public string Format(string permission) => new PermissionPolicyCodec().Format(PermissionPolicyDescriptor.Single(permission));

    public bool TryParse(string policyName, out string permission)
    {
        var result = new PermissionPolicyCodec().Parse(policyName);
        if (result is { Status: PermissionPolicyParseStatus.Valid, Descriptor.Mode: PermissionRequirementMode.Single })
        {
            permission = result.Descriptor.Permissions[0];
            return true;
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
        Policy = new PermissionPolicyCodec().Format(PermissionPolicyDescriptor.Single(permission));
    }

    public string Permission { get; }
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

internal sealed record NormalizedPermissionPrincipalRequirement : IAuthorizationRequirement;

public sealed class RequirePermissionPolicyProvider(
    IOptions<AuthorizationOptions> options,
    IPermissionPolicyCodec codec,
    IPermissionPolicyNameFormatter formatter,
    AuthorizationPolicyProviderFallback? fallback = null) : IAuthorizationPolicyProvider
{
    private readonly DefaultAuthorizationPolicyProvider _defaultProvider = new(options);

    public RequirePermissionPolicyProvider(
        IOptions<AuthorizationOptions> options,
        IPermissionPolicyNameFormatter formatter,
        AuthorizationPolicyProviderFallback? fallback = null)
        : this(options, new PermissionPolicyCodec(), formatter, fallback)
    {
    }

    public async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        var parseResult = codec.Parse(policyName);
        if (parseResult.Status == PermissionPolicyParseStatus.MalformedReservedPolicy)
            return null;

        PermissionPolicyDescriptor descriptor;
        if (parseResult is { Status: PermissionPolicyParseStatus.Valid, Descriptor: not null })
            descriptor = parseResult.Descriptor;
        else if (formatter.TryParse(policyName, out var permission))
            descriptor = PermissionPolicyDescriptor.Single(permission);
        else
            return fallback is not null ? await fallback.Provider.GetPolicyAsync(policyName) : await _defaultProvider.GetPolicyAsync(policyName);

        IAuthorizationRequirement permissionRequirement = descriptor.Mode == PermissionRequirementMode.Single
            ? new PermissionAuthorizationRequirement(descriptor.Permissions[0])
            : new PermissionSetAuthorizationRequirement(descriptor.Mode, descriptor.Permissions);

        return new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new NormalizedPermissionPrincipalRequirement(), permissionRequirement)
            .Build();
    }

    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() =>
        fallback?.Provider.GetDefaultPolicyAsync() ?? _defaultProvider.GetDefaultPolicyAsync();

    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() =>
        fallback?.Provider.GetFallbackPolicyAsync() ?? _defaultProvider.GetFallbackPolicyAsync();
}

public class PermissionAuthorizationHandler : AuthorizationHandler<PermissionAuthorizationRequirement>
{
    private readonly IPermissionAuthorizationService _authorization;

    public PermissionAuthorizationHandler(IPermissionEvaluator evaluator, IEnumerable<IPermissionResourceHandler> resourceHandlers)
    {
        _authorization = new PermissionAuthorizationService(evaluator, resourceHandlers, validator: null, httpContextAccessor: null);
    }

    public PermissionAuthorizationHandler(
        IPermissionAuthorizationService authorization)
    {
        _authorization = authorization;
    }

    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, PermissionAuthorizationRequirement requirement)
    {
        var result = await _authorization.AuthorizeAsync(
            new PermissionEvaluationContext(
                context.User,
                PermissionKey.Normalize(requirement.Permission),
                TenantId: null,
                context.Resource));

        if (result.Succeeded)
            context.Succeed(requirement);
        else
            context.Fail();
    }
}

// Keep the source-compatible public handler constructor while giving the built-in DI registration
// one unambiguous constructor that always reaches the canonical authorization service.
internal sealed class RegisteredPermissionAuthorizationHandler(IPermissionAuthorizationService authorization)
    : PermissionAuthorizationHandler(authorization);

internal sealed class PermissionSetAuthorizationHandler(
    IPermissionAuthorizationService authorization)
    : AuthorizationHandler<PermissionSetAuthorizationRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionSetAuthorizationRequirement requirement)
    {
        if (requirement.Mode is not (PermissionRequirementMode.Any or PermissionRequirementMode.All))
        {
            context.Fail();
            return;
        }

        foreach (var permission in requirement.Permissions)
        {
            var outcome = await authorization.AuthorizeAsync(
                new PermissionEvaluationContext(context.User, permission, TenantId: null, context.Resource));

            if (requirement.Mode == PermissionRequirementMode.Any && outcome.Succeeded)
            {
                context.Succeed(requirement);
                return;
            }

            if (requirement.Mode == PermissionRequirementMode.All && !outcome.Succeeded)
            {
                context.Fail();
                return;
            }
        }

        if (requirement.Mode == PermissionRequirementMode.All)
            context.Succeed(requirement);
        else
            context.Fail();
    }
}

public sealed class PermissionAuthorizationService(
    IPermissionEvaluator evaluator,
    IEnumerable<IPermissionResourceHandler> resourceHandlers,
    NormalizedPrincipalValidator? validator,
    IHttpContextAccessor? httpContextAccessor) : IPermissionAuthorizationService
{
    public async ValueTask<PermissionEvaluationResult> AuthorizeAsync(
        PermissionEvaluationContext context,
        CancellationToken cancellationToken = default)
    {
        if (validator is null || !validator.TryGetNormalizedPrincipal(context.Principal, out var principal))
            return PermissionEvaluationResult.Denied("The principal is not a trusted normalized Elsa principal.");

        var httpContext = context.Resource as HttpContext ?? httpContextAccessor?.HttpContext;
        var tokens = new List<CancellationToken>(3);
        AddToken(tokens, cancellationToken);
        AddToken(tokens, context.CancellationToken);
        AddToken(tokens, httpContext?.RequestAborted ?? CancellationToken.None);
        using var linkedCancellation = tokens.Count > 1
            ? CancellationTokenSource.CreateLinkedTokenSource(tokens.ToArray())
            : null;
        var requestCancellation = linkedCancellation?.Token ?? (tokens.Count == 1 ? tokens[0] : CancellationToken.None);
        requestCancellation.ThrowIfCancellationRequested();

        var tenantId = context.TenantId ?? ResolveTenantId(context.Resource, principal);
        var evaluationContext = context with
        {
            Principal = principal,
            TenantId = tenantId,
            CancellationToken = requestCancellation
        };
        var resourceDenied = false;
        var resourceGranted = false;

        foreach (var resourceHandler in resourceHandlers)
        {
            requestCancellation.ThrowIfCancellationRequested();
            var resourceResult = await resourceHandler.EvaluateAsync(evaluationContext, requestCancellation);
            requestCancellation.ThrowIfCancellationRequested();
            if (resourceResult is null)
                continue;

            if (resourceResult.Succeeded)
                resourceGranted = true;
            else
                resourceDenied = true;
        }

        if (resourceDenied)
            return PermissionEvaluationResult.Denied("A permission resource denied the request.");
        if (resourceGranted)
            return PermissionEvaluationResult.Success;

        requestCancellation.ThrowIfCancellationRequested();
        var result = await evaluator.EvaluateAsync(evaluationContext, requestCancellation);
        requestCancellation.ThrowIfCancellationRequested();
        return result;
    }

    private static void AddToken(ICollection<CancellationToken> tokens, CancellationToken token)
    {
        if (token != CancellationToken.None && !tokens.Contains(token))
            tokens.Add(token);
    }

    private static string? ResolveTenantId(object? resource, ClaimsPrincipal principal)
    {
        if (resource is not null)
        {
            var property = resource.GetType().GetProperty("TenantId");
            if (property?.GetValue(resource) is string resourceTenantId && !string.IsNullOrWhiteSpace(resourceTenantId))
                return resourceTenantId;
        }

        return principal.FindFirst(IdentityClaimTypes.TenantId)?.Value;
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
        identity.AddClaim(new Claim(IdentityClaimTypes.Normalized, "v1"));

        foreach (var role in roles.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            identity.AddClaim(new Claim(IdentityClaimTypes.Role, role));

        foreach (var permission in permissions.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            identity.AddClaim(new Claim(IdentityClaimTypes.Permission, permission));

        return ValueTask.FromResult(new ClaimsNormalizationResult(new ClaimsPrincipal(identity), roles, permissions));
    }

    private static bool IsInternalIdentityClaim(string claimType) =>
        claimType is IdentityClaimTypes.Normalized or IdentityClaimTypes.TenantId or IdentityClaimTypes.Provider or IdentityClaimTypes.Role or IdentityClaimTypes.Permission;
}
