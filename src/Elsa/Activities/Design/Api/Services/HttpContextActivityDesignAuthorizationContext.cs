using Elsa.Activities.Design.Api.Authorization;
using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace Elsa.Activities.Design.Api.Services;

#pragma warning disable CS0618 // The implementation intentionally bridges the obsolete host contract.

/// <summary>
/// Default HTTP request adapter for reusable-activity authoring and dependency visibility.
/// Permission decisions use the Foundation Identity authorization service. The synchronous
/// interface implementations are retained only as an advisory compatibility window and fail
/// closed; first-party callers use the asynchronous sibling seams.
/// </summary>
public sealed class HttpContextActivityDesignAuthorizationContext :
    IActivityAuthoringContext,
    IActivityDependencyAuthorizationContext,
    IActivityAuthoringContextAsync,
    IActivityDependencyContextAsync
{
    public const string AuthorPermission = ActivityDesignPermissions.AuthorProvider;
    public const string ProviderPayloadReadPermission = ActivityDesignPermissions.ReadProviderPayload;
    public const string ActivityDesignManagePermission = ActivityDesignPermissions.Manage;

    private readonly IPermissionAuthorizationService _authorization;
    private readonly NormalizedPrincipalValidator _principalValidator;
    private readonly ClaimsPrincipal _principal;
    private readonly bool _trusted;
    private readonly string? _tenantId;
    private readonly string _actorId;
    private readonly CancellationToken _requestCancellationToken;
    private Lazy<Task<AuthorizationSnapshot>>? _snapshot;

    [Obsolete("Use the Foundation Identity-enabled constructor selected by dependency injection.")]
    public HttpContextActivityDesignAuthorizationContext(IHttpContextAccessor httpContextAccessor)
    {
        ArgumentNullException.ThrowIfNull(httpContextAccessor);
        _authorization = null!;
        _principalValidator = null!;
        _principal = new ClaimsPrincipal(new ClaimsIdentity());
        _trusted = false;
        _tenantId = null;
        _actorId = string.Empty;
        _requestCancellationToken = CancellationToken.None;
    }

    [ActivatorUtilitiesConstructor]
    public HttpContextActivityDesignAuthorizationContext(
        IHttpContextAccessor httpContextAccessor,
        IPermissionAuthorizationService authorization,
        NormalizedPrincipalValidator principalValidator)
    {
        ArgumentNullException.ThrowIfNull(httpContextAccessor);
        _authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
        _principalValidator = principalValidator ?? throw new ArgumentNullException(nameof(principalValidator));

        var httpContext = httpContextAccessor.HttpContext;
        var rawPrincipal = httpContext?.User ?? new ClaimsPrincipal(new ClaimsIdentity());
        _trusted = _principalValidator.TryGetNormalizedPrincipal(rawPrincipal, out var normalizedPrincipal);
        _principal = _trusted ? normalizedPrincipal : new ClaimsPrincipal(new ClaimsIdentity());
        _tenantId = _trusted ? FindTenantId(_principal) : null;
        _actorId = _trusted ? FindActorId(_principal) : string.Empty;
        _requestCancellationToken = httpContext?.RequestAborted ?? CancellationToken.None;
    }

    public string? TenantId => _tenantId;

    [Obsolete("Use IActivityAuthoringContextAsync.GetAuthorizationProfileAsync.")]
    public string AuthorizationProfile => throw SynchronousAccess();

    [Obsolete("Use IActivityAuthoringContextAsync.ActorId.")]
    public string ActorId => _actorId;

    [Obsolete("Use IActivityAuthoringContextAsync.CanAuthorProviderAsync.")]
    public bool CanAuthorProvider(string providerKey) => throw SynchronousAccess();

    [Obsolete("Use IActivityAuthoringContextAsync.CanReadProviderPayloadAsync.")]
    public bool CanReadProviderPayload(string providerKey) => throw SynchronousAccess();

    [Obsolete("Use IActivityAuthoringContextAsync.CanManageActivityDefinitionsAsync.")]
    public bool CanManageActivityDefinitions => throw SynchronousAccess();

    [Obsolete("Use IActivityDependencyContextAsync.CanReadAsync.")]
    public bool CanRead(ActivityDefinitionReference reference) => throw SynchronousAccess();

    public async ValueTask<string> GetAuthorizationProfileAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        return snapshot.Profile;
    }

    public ValueTask<bool> CanAuthorProviderAsync(string providerKey, CancellationToken cancellationToken = default) =>
        !_trusted || string.IsNullOrWhiteSpace(providerKey)
            ? ValueTask.FromResult(false)
            : AuthorizeProviderAsync(AuthorPermission, providerKey, cancellationToken);

    public ValueTask<bool> CanReadProviderPayloadAsync(string providerKey, CancellationToken cancellationToken = default) =>
        !_trusted || string.IsNullOrWhiteSpace(providerKey)
            ? ValueTask.FromResult(false)
            : AuthorizeProviderAsync(ProviderPayloadReadPermission, providerKey, cancellationToken);

    public ValueTask<bool> CanManageActivityDefinitionsAsync(CancellationToken cancellationToken = default) =>
        EvaluateSnapshotAsync(snapshot => snapshot.CanManage, cancellationToken);

    public ValueTask<bool> CanReadAsync(ActivityDefinitionReference reference, CancellationToken cancellationToken = default) =>
        cancellationToken.IsCancellationRequested
            ? ValueTask.FromCanceled<bool>(cancellationToken)
            : ValueTask.FromResult(_trusted && (reference.TenantId is null || StringComparer.Ordinal.Equals(reference.TenantId, _tenantId)));

    private async ValueTask<bool> EvaluateSnapshotAsync(
        Func<AuthorizationSnapshot, bool> selector,
        CancellationToken cancellationToken)
    {
        var snapshot = await GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        return selector(snapshot);
    }

    private async Task<AuthorizationSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        if (!_trusted)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return DeniedSnapshot;
        }

        var existing = Volatile.Read(ref _snapshot);
        if (existing is null)
        {
            var created = new Lazy<Task<AuthorizationSnapshot>>(
                CreateSnapshotAsync,
                LazyThreadSafetyMode.ExecutionAndPublication);
            existing = Interlocked.CompareExchange(ref _snapshot, created, null) ?? created;
        }

        var snapshotTask = existing.Value;
        try
        {
            // The computation is shared independently of any one caller. A canceled waiter
            // must not poison the cached snapshot for concurrent callers; cancellation is
            // applied to each wait instead.
            return await snapshotTask.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            if (snapshotTask.IsCanceled || snapshotTask.IsFaulted)
                Interlocked.CompareExchange(ref _snapshot, null, existing);
            throw;
        }
    }

    private async Task<AuthorizationSnapshot> CreateSnapshotAsync()
    {
        var canAuthor = await AuthorizeAsync(AuthorPermission, _requestCancellationToken).ConfigureAwait(false);
        var canReadProviderPayload = await AuthorizeAsync(ProviderPayloadReadPermission, _requestCancellationToken).ConfigureAwait(false);
        var canManage = await AuthorizeAsync(ActivityDesignManagePermission, _requestCancellationToken).ConfigureAwait(false);
        var profileMaterial = $"tenant:{_tenantId ?? "global"}|author:{canAuthor}|provider-payload:{canReadProviderPayload}|manage:{canManage}";
        var profile = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(profileMaterial))).ToLowerInvariant();
        return new AuthorizationSnapshot(canAuthor, canReadProviderPayload, canManage, profile);
    }

    private async ValueTask<bool> AuthorizeAsync(string permission, CancellationToken cancellationToken)
    {
        var result = await _authorization.AuthorizeAsync(
            new PermissionEvaluationContext(_principal, permission, _tenantId),
            cancellationToken).ConfigureAwait(false);
        return result.Succeeded;
    }

    private async ValueTask<bool> AuthorizeProviderAsync(
        string permission,
        string providerKey,
        CancellationToken cancellationToken)
    {
        var result = await _authorization.AuthorizeAsync(
            new PermissionEvaluationContext(
                _principal,
                permission,
                _tenantId,
                new ActivityProviderAuthorizationResource(providerKey, _tenantId)),
            cancellationToken).ConfigureAwait(false);
        return result.Succeeded;
    }

    private static string? FindTenantId(ClaimsPrincipal principal) =>
        principal.FindFirst(IdentityClaimTypes.TenantId)?.Value
        ?? principal.FindFirst("tenant_id")?.Value;

    private static string FindActorId(ClaimsPrincipal principal) =>
        principal.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? principal.FindFirst("sub")?.Value
        ?? principal.Identity?.Name
        ?? string.Empty;

    private static InvalidOperationException SynchronousAccess() =>
        new("Synchronous activity authorization access is obsolete and intentionally unavailable. Use the asynchronous authorization context.");

    private sealed record AuthorizationSnapshot(
        bool CanAuthor,
        bool CanReadProviderPayload,
        bool CanManage,
        string Profile);

    private static readonly AuthorizationSnapshot DeniedSnapshot =
        new(false, false, false, "untrusted");
}

public sealed class LegacyActivityAuthoringContextAdapter : IActivityAuthoringContextAsync
{
    private readonly IActivityAuthoringContext _legacy;

    public LegacyActivityAuthoringContextAdapter(IActivityAuthoringContext legacy) =>
        _legacy = legacy ?? throw new ArgumentNullException(nameof(legacy));

    public string? TenantId => _legacy.TenantId;
    public string ActorId => _legacy.ActorId;
    public ValueTask<string> GetAuthorizationProfileAsync(CancellationToken cancellationToken = default) =>
        cancellationToken.IsCancellationRequested ? ValueTask.FromCanceled<string>(cancellationToken) : ValueTask.FromResult(_legacy.AuthorizationProfile);
    public ValueTask<bool> CanAuthorProviderAsync(string providerKey, CancellationToken cancellationToken = default) =>
        cancellationToken.IsCancellationRequested ? ValueTask.FromCanceled<bool>(cancellationToken) : ValueTask.FromResult(_legacy.CanAuthorProvider(providerKey));
    public ValueTask<bool> CanReadProviderPayloadAsync(string providerKey, CancellationToken cancellationToken = default) =>
        cancellationToken.IsCancellationRequested ? ValueTask.FromCanceled<bool>(cancellationToken) : ValueTask.FromResult(_legacy.CanReadProviderPayload(providerKey));
    public ValueTask<bool> CanManageActivityDefinitionsAsync(CancellationToken cancellationToken = default) =>
        cancellationToken.IsCancellationRequested ? ValueTask.FromCanceled<bool>(cancellationToken) : ValueTask.FromResult(_legacy.CanManageActivityDefinitions);
}

public sealed class LegacyActivityDependencyContextAdapter : IActivityDependencyContextAsync
{
    private readonly IActivityDependencyAuthorizationContext _legacy;

    public LegacyActivityDependencyContextAdapter(IActivityDependencyAuthorizationContext legacy) =>
        _legacy = legacy ?? throw new ArgumentNullException(nameof(legacy));

    public string? TenantId => _legacy.TenantId;
    public ValueTask<string> GetAuthorizationProfileAsync(CancellationToken cancellationToken = default) =>
        cancellationToken.IsCancellationRequested ? ValueTask.FromCanceled<string>(cancellationToken) : ValueTask.FromResult(_legacy.AuthorizationProfile);
    public ValueTask<bool> CanReadAsync(ActivityDefinitionReference reference, CancellationToken cancellationToken = default) =>
        cancellationToken.IsCancellationRequested ? ValueTask.FromCanceled<bool>(cancellationToken) : ValueTask.FromResult(_legacy.CanRead(reference));
}

#pragma warning restore CS0618
