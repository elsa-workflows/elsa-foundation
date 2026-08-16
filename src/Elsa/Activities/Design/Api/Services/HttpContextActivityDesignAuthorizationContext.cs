using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Microsoft.AspNetCore.Http;

namespace Elsa.Activities.Design.Api.Services;

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
    IActivityDependencyAuthorizationContextAsync
{
    public const string AuthorPermission = "activities.design.author";
    public const string ProviderPayloadReadPermission = "activities.design.provider-payload.read";
    public const string ActivityDesignManagePermission = "activity-design.manage";

    private readonly IPermissionAuthorizationService _authorization;
    private readonly ClaimsPrincipal _principal;
    private readonly string? _tenantId;
    private readonly string _actorId;
    private Lazy<Task<AuthorizationSnapshot>>? _snapshot;

    public HttpContextActivityDesignAuthorizationContext(
        IHttpContextAccessor httpContextAccessor,
        IPermissionAuthorizationService authorization)
    {
        ArgumentNullException.ThrowIfNull(httpContextAccessor);
        _authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));

        var httpContext = httpContextAccessor.HttpContext;
        _principal = httpContext?.User is { } user
            ? new ClaimsPrincipal(user)
            : new ClaimsPrincipal(new ClaimsIdentity());
        _tenantId = FindTenantId(_principal);
        _actorId = FindActorId(_principal);
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

    [Obsolete("Use IActivityDependencyAuthorizationContextAsync.CanReadAsync.")]
    public bool CanRead(ActivityDefinitionReference reference) => throw SynchronousAccess();

    public async ValueTask<string> GetAuthorizationProfileAsync(CancellationToken cancellationToken = default)
    {
        var snapshot = await GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        return snapshot.Profile;
    }

    public ValueTask<bool> CanAuthorProviderAsync(string providerKey, CancellationToken cancellationToken = default) =>
        string.IsNullOrWhiteSpace(providerKey)
            ? ValueTask.FromResult(false)
            : AuthorizeProviderAsync(AuthorPermission, providerKey, cancellationToken);

    public ValueTask<bool> CanReadProviderPayloadAsync(string providerKey, CancellationToken cancellationToken = default) =>
        string.IsNullOrWhiteSpace(providerKey)
            ? ValueTask.FromResult(false)
            : AuthorizeProviderAsync(ProviderPayloadReadPermission, providerKey, cancellationToken);

    public ValueTask<bool> CanManageActivityDefinitionsAsync(CancellationToken cancellationToken = default) =>
        EvaluateSnapshotAsync(snapshot => snapshot.CanManage, cancellationToken);

    public ValueTask<bool> CanReadAsync(ActivityDefinitionReference reference, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(reference.TenantId is null || StringComparer.Ordinal.Equals(reference.TenantId, _tenantId));

    private async ValueTask<bool> EvaluateSnapshotAsync(
        Func<AuthorizationSnapshot, bool> selector,
        CancellationToken cancellationToken)
    {
        var snapshot = await GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
        return selector(snapshot);
    }

    private Task<AuthorizationSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        var existing = Volatile.Read(ref _snapshot);
        if (existing is not null)
            return existing.Value;

        var created = new Lazy<Task<AuthorizationSnapshot>>(
            () => CreateSnapshotAsync(cancellationToken),
            LazyThreadSafetyMode.ExecutionAndPublication);
        var winner = Interlocked.CompareExchange(ref _snapshot, created, null) ?? created;
        return winner.Value;
    }

    private async Task<AuthorizationSnapshot> CreateSnapshotAsync(CancellationToken cancellationToken)
    {
        var canAuthor = await AuthorizeAsync(AuthorPermission, cancellationToken).ConfigureAwait(false);
        var canReadProviderPayload = await AuthorizeAsync(ProviderPayloadReadPermission, cancellationToken).ConfigureAwait(false);
        var canManage = await AuthorizeAsync(ActivityDesignManagePermission, cancellationToken).ConfigureAwait(false);
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
}
