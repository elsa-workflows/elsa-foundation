using System.Security.Claims;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Microsoft.AspNetCore.Http;

namespace Elsa.Workflows.Runtime.Api.StandaloneTriggers;

internal static class TriggerPermissions
{
    internal const string Read = "triggers:read", Manage = "triggers:manage", Operate = "triggers:operate",
        Authorize = "triggers:authorize", DeliveriesRead = "triggers/deliveries:read",
        Retry = "triggers/deliveries:retry", Cancel = "triggers/deliveries:cancel";
}

internal sealed class TriggerPermissionContributor : IPermissionContributor
{
    public string OwnerId => StandaloneTriggersApi.Owner;
    public IEnumerable<Permission> Contribute()
    {
        yield return new(TriggerPermissions.Read, "Read triggers", "Triggers", "Read permitted trigger resources and associations.");
        yield return new(TriggerPermissions.Manage, "Manage triggers", "Triggers", "Manage permitted configuration and associations.");
        yield return new(TriggerPermissions.Operate, "Operate triggers", "Triggers", "Enable and pause permitted resources.");
        yield return new(TriggerPermissions.Authorize, "Authorize triggers", "Triggers", "Grant and revoke resource invocation authority.");
        yield return new(TriggerPermissions.DeliveriesRead, "Read trigger deliveries", "Triggers", "Inspect permitted retained delivery evidence.");
        yield return new(TriggerPermissions.Retry, "Retry trigger deliveries", "Triggers", "Request recovery of owed recipients.");
        yield return new(TriggerPermissions.Cancel, "Cancel pending trigger work", "Triggers", "Cancel recipient work before dispatch commitment.");
    }
}

internal interface ITriggerRequestAccess
{
    string Subject { get; }
    TriggerScope Scope { get; }
    ValueTask<bool> HasAsync(string permission, object? resource, CancellationToken cancellationToken);
    ValueTask RequireAsync(string permission, object? resource, CancellationToken cancellationToken, bool mask = false);
    ValueTask<bool> CanReadAsync(object? resource, CancellationToken cancellationToken);
    ValueTask<TriggerActor> ActorAsync(CancellationToken cancellationToken);
}

internal sealed class TriggerRequestAccess(IHttpContextAccessor http, NormalizedPrincipalValidator validator,
    IPermissionAuthorizationService authorization, IPersistenceAccessContextAccessor persistence) : ITriggerRequestAccess
{
    private ClaimsPrincipal Principal
    {
        get
        {
            if (http.HttpContext is not { } context || !validator.TryGetNormalizedPrincipal(context.User, out var principal))
                throw new TriggerOperationException("unauthorized", "Sign in to use triggers.");
            return principal;
        }
    }
    public string Subject
    {
        get
        {
            var subjects = Principal.FindAll(IdentityClaimTypes.UserId).Select(claim => claim.Value).ToArray();
            if (subjects.Length != 1 || string.IsNullOrWhiteSpace(subjects[0]))
                throw new TriggerOperationException("forbidden", "A canonical user identity is required.");
            return subjects[0];
        }
    }
    public TriggerScope Scope
    {
        get
        {
            var tenants = Principal.FindAll(IdentityClaimTypes.TenantId).Select(claim => claim.Value).ToArray();
            if (tenants.Length > 1 || tenants.Any(string.IsNullOrWhiteSpace))
                throw new TriggerOperationException("forbidden", "A canonical tenant identity is required.");
            var context = persistence.Current;
            if (context.Scope is null || context.AcrossScopes || context.AccessPolicy != Elsa.Workflows.Runtime.Core.Models.PersistenceAccessPolicy.Ordinary)
                throw new TriggerOperationException("unavailable", "Select one ordinary persistence scope.");
            var scope = new TriggerScope(context.Scope, tenants.SingleOrDefault());
            return scope;
        }
    }
    public async ValueTask<bool> HasAsync(string permission, object? resource, CancellationToken cancellationToken) =>
        (await authorization.AuthorizeAsync(new(Principal, permission, Scope.TenantId, resource), cancellationToken)).Succeeded;
    public async ValueTask RequireAsync(string permission, object? resource, CancellationToken cancellationToken, bool mask = false)
    {
        _ = Subject;
        if (!await HasAsync(permission, resource, cancellationToken))
            throw new TriggerOperationException(mask ? "missing" : "forbidden", mask ? "Trigger resource not found." : "This trigger operation is not permitted.");
    }
    public async ValueTask<bool> CanReadAsync(object? resource, CancellationToken cancellationToken) =>
        await HasAsync(TriggerPermissions.Read, resource, cancellationToken) || await HasAsync(TriggerPermissions.Manage, resource, cancellationToken) ||
        await HasAsync(TriggerPermissions.Operate, resource, cancellationToken);
    public async ValueTask<TriggerActor> ActorAsync(CancellationToken cancellationToken) => new(Subject,
        await CanReadAsync(null, cancellationToken), await HasAsync(TriggerPermissions.Manage, null, cancellationToken),
        await HasAsync(TriggerPermissions.Operate, null, cancellationToken), await HasAsync(TriggerPermissions.Authorize, null, cancellationToken),
        await HasAsync(TriggerPermissions.DeliveriesRead, null, cancellationToken), await HasAsync(TriggerPermissions.Retry, null, cancellationToken),
        await HasAsync(TriggerPermissions.Cancel, null, cancellationToken));
}
