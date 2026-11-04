using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Foundation.Identity.Core.Authentication;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.Core.Ownership;
using Elsa.Foundation.Identity.Core.Security;
using Elsa.Foundation.Identity.ExternalAuthentication.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NativeEndpoints;

namespace Elsa.Foundation.Identity.ExternalAuthentication.Api;

public static partial class ExternalAuthenticationApi
{
    private static void MapIdentityAdministration(IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapEndpointGroup(typeof(ExternalAuthenticationApiFeature).Assembly.GetName().Name!, ExternalAuthenticationApiJsonContext.Default, tag: "Identity");
        Map("GET", "/users/{userId}/sessions", "Sessions", typeof(ManagedSessionPage), InspectSessions,
            DefaultIdentityPermissionKeys.IdentitySessionsRead, DefaultIdentityPermissionKeys.IdentityUsersRead);
        Map("GET", "/users/{userId}/links", "Links", typeof(ManagedIdentityLinkPage), InspectLinks,
            DefaultIdentityPermissionKeys.IdentityLinksRead, DefaultIdentityPermissionKeys.IdentityUsersRead);
        Map("POST", "/users/{userId}/sessions/{sessionId}/revoke", "RevokeSession", typeof(ManagedSessionRevokeResponse), RevokeSession,
            DefaultIdentityPermissionKeys.IdentitySessionsManage, DefaultIdentityPermissionKeys.IdentityUsersRead, typeof(ManagedSessionRevokeRequest));
        Map("POST", "/users/{userId}/links/attach", "AttachLink", typeof(ManagedLinkAttachResponse), AttachLink,
            DefaultIdentityPermissionKeys.IdentityLinksManage, DefaultIdentityPermissionKeys.IdentityUsersRead, typeof(ManagedLinkAttachRequest));

        void Map(string method, string suffix, string name, Type response, Func<HttpContext, Task> handler,
            string permission, string otherPermission, Type? request = null)
        {
            var endpoint = api.MapUnboundOperation(method, ManagementPath + suffix, "ExternalIdentity" + name, response, 200, null,
                    context => Run(context, handler, 65536), name: "ExternalIdentity" + name, containFailures: false)
                .DisableCookieRedirect().RequireAllPermissions(permission, otherPermission);
            if (request is not null) endpoint.WithMetadata(new AcceptsMetadata(["application/json"], request, false));
        }
    }

    private static async Task InspectSessions(HttpContext context)
    {
        if (!await CurrentPermission(context, DefaultIdentityPermissionKeys.IdentitySessionsRead) ||
            !await CurrentPermission(context, DefaultIdentityPermissionKeys.IdentityUsersRead)) return;
        var page = Page(context); var userId = Route(context, "userId");
        if (page is null || !IdentityText(userId)) { await ManagementFailure(context, "invalid-request", 400); return; }
        await using var scope = ManagementScope(context); var services = scope.ServiceProvider;
        if (await services.GetRequiredService<IUserStore>().FindAsync(Tenant(context), userId, context.RequestAborted) is null)
        { await ManagementFailure(context, "user-not-found", 404); return; }
        var store = services.GetService<IAuthenticationSessionStore>();
        var canonical = services.GetService<ICanonicalUserSignInService>();
        if (store?.IsDurable != true || canonical?.SupportsDurableSessions != true)
        { await ManagementFailure(context, "session-administration-unavailable", 503); return; }
        try
        {
            var records = await store.ListAsync(Tenant(context), userId, page, context.RequestAborted);
            if (records.Items.Count > page.Take || records.Items.Any(item => item.TenantId != Tenant(context) || item.UserId != userId))
            { await ManagementFailure(context, "session-administration-unavailable", 503); return; }
            var canRevoke = await SessionRevocationPermitted(context, services);
            var current = context.User.FindAll(IdentityClaimTypes.SessionId).ToArray();
            var now = services.GetRequiredService<TimeProvider>().GetUtcNow();
            var items = new List<ManagedSessionResponse>();
            foreach (var item in records.Items)
            {
                var state = item.RevokedAt is not null ? "revoked" : item.ExpiresAt <= now ? "expired" :
                    item.CreatedAt > now || !await canonical.ValidateSessionAsync(item, context.RequestAborted) ? "inactive" : "active";
                items.Add(new(item.Id, item.UserId, item.Provider, item.ConnectionKey, item.ConnectionRecordId, item.Issuer,
                    item.CreatedAt, item.ExpiresAt, item.RevokedAt, state, item.Revision!,
                    current.Length == 1 && current[0].Value == item.Id, canRevoke && state != "revoked"));
            }
            await ManagementJson(context, new ManagedSessionPage(userId, items, records.TotalCount), ExternalAuthenticationApiJsonContext.Default.ManagedSessionPage);
        }
        catch (Exception exception) when (!(exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested))
        { await ManagementFailure(context, "session-administration-unavailable", 503); }
    }

    private static async Task InspectLinks(HttpContext context)
    {
        if (!await CurrentPermission(context, DefaultIdentityPermissionKeys.IdentityLinksRead) ||
            !await CurrentPermission(context, DefaultIdentityPermissionKeys.IdentityUsersRead)) return;
        var page = Page(context); var userId = Route(context, "userId");
        if (page is null || !IdentityText(userId)) { await ManagementFailure(context, "invalid-request", 400); return; }
        await using var scope = ManagementScope(context); var services = scope.ServiceProvider;
        if (await services.GetRequiredService<IUserStore>().FindAsync(Tenant(context), userId, context.RequestAborted) is null)
        { await ManagementFailure(context, "user-not-found", 404); return; }
        var store = services.GetService<IPagedExternalIdentityStore>();
        if (store is null) { await ManagementFailure(context, "link-administration-unavailable", 503); return; }
        var user = services.GetService<IRevisionAwareUserStore>() is { } userRevisions
            ? await userRevisions.FindWithRevisionAsync(Tenant(context), userId, context.RequestAborted) : null;
        var links = await store.ListForUserPageAsync(Tenant(context), userId, page, context.RequestAborted);
        if (links.Items.Count > page.Take || links.Items.Any(item => item.TenantId != Tenant(context) || item.UserId != userId))
        { await ManagementFailure(context, "link-administration-unavailable", 503); return; }
        var registry = await context.RequestServices.GetRequiredService<IConnectionRegistry>().ReadAsync(context.RequestAborted);
        var revisions = services.GetService<IRevisionAwareExternalIdentityStore>();
        var items = new List<ManagedIdentityLinkResponse>();
        foreach (var link in links.Items)
        {
            var connection = registry.Connections.Where(view => view.Record.Key == link.Provider)
                .OrderByDescending(view => view.Effective).FirstOrDefault();
            var revision = revisions is null ? null : link.Issuer is null
                ? await revisions.FindBySubjectWithRevisionAsync(Tenant(context), link.Provider, link.ProviderSubject, context.RequestAborted)
                : await revisions.FindByKeyWithRevisionAsync(new(Tenant(context), link.Provider, link.Issuer, link.ProviderSubject), context.RequestAborted);
            // A link may change owner between the page and its revision read. Never combine the old owner
            // projection with another owner's current revision; that would be an unsafe mutation affordance.
            var matching = revision?.Record.UserId == userId;
            items.Add(new(link.Provider, link.Issuer, link.ProviderSubject, link.LinkedAt, link.LastSeenAt,
                matching ? revision!.Revision : null, link.Issuer is not null, connection is not null, connection?.Record.DisplayName));
        }
        var membership = await services.GetRequiredService<ITenantMembershipStore>().FindAsync(Tenant(context), userId, context.RequestAborted);
        var canAttach = user is not null && await LinkAttachmentPermitted(context, services, user.Record, membership);
        var choices = canAttach ? registry.Connections.Where(view => view.Record.ArchivedAt is null)
            .Select(view => new ManagedLinkConnectionChoice(view.Record.Id, view.Record.Key, view.Record.DisplayName, view.Effective, view.Record.Enabled)).ToArray() : [];
        await ManagementJson(context, new ManagedIdentityLinkPage(userId, items, links.TotalCount, user?.Revision, registry.Revision, canAttach, choices), ExternalAuthenticationApiJsonContext.Default.ManagedIdentityLinkPage);
    }

    private static async Task AttachLink(HttpContext context)
    {
        if (!await CurrentPermission(context, DefaultIdentityPermissionKeys.IdentityLinksManage) ||
            !await CurrentPermission(context, DefaultIdentityPermissionKeys.IdentityUsersRead) || !await ManagementPost(context)) return;
        var userId = Route(context, "userId");
        var request = await context.Request.ReadFromJsonAsync(ExternalAuthenticationApiJsonContext.Default.ManagedLinkAttachRequest, context.RequestAborted);
        if (!IdentityText(userId) || request is null || !IdentityText(request.Subject) || !IdentityText(request.ConnectionRecordId) || !IdentityText(request.ExpectedUserRevision))
        { await ManagementFailure(context, "invalid-request", 400); return; }
        await using var scope = ManagementScope(context); var services = scope.ServiceProvider;
        var revisions = services.GetService<IRevisionAwareUserStore>(); var links = services.GetService<IExternalIdentityStore>();
        if (revisions is null || links?.SupportsAtomicAdmission != true || !links.SupportsQualifiedKeys)
        { await ManagementFailure(context, "link-administration-unavailable", 503); return; }
        var user = await revisions.FindWithRevisionAsync(Tenant(context), userId, context.RequestAborted);
        if (user is null) { await ManagementFailure(context, "user-not-found", 404); return; }
        if (user.Revision != request.ExpectedUserRevision) { await ManagementFailure(context, "revision-conflict", 409); return; }
        var membership = await services.GetRequiredService<ITenantMembershipStore>().FindAsync(Tenant(context), userId, context.RequestAborted);
        if (!await LinkAttachmentPermitted(context, services, user.Record, membership)) { await ManagementFailure(context, "link-delegation-forbidden", 403); return; }
        var registry = context.RequestServices.GetRequiredService<IConnectionRegistry>();
        var catalog = await registry.ReadAsync(context.RequestAborted);
        if (catalog.Revision != request.ExpectedCatalogRevision) { await ManagementFailure(context, "revision-conflict", 409); return; }
        var connection = catalog.Connections.SingleOrDefault(view => view.Record.Id == request.ConnectionRecordId && view.Record.ArchivedAt is null);
        if (connection is null) { await ManagementFailure(context, "connection-not-found", 404); return; }
        var protocols = services.GetServices<IExternalAuthenticationProtocol>().Where(item => item.Name == connection.Record.Protocol).Take(2).ToArray();
        if (protocols.Length != 1) { await ManagementFailure(context, "link-administration-unavailable", 503); return; }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted); timeout.CancelAfter(TimeSpan.FromSeconds(30));
        ExternalProtocolCheck tested;
        try { tested = await protocols[0].TestAsync(connection.Record, timeout.Token); }
        catch (OperationCanceledException) when (!context.RequestAborted.IsCancellationRequested)
        { await ManagementFailure(context, "provider-test-timeout", 504); return; }
        if (tested.Failure != ExternalProtocolFailure.None || string.IsNullOrWhiteSpace(tested.Issuer))
        { await ManagementFailure(context, "connection-not-ready", 409); return; }
        // Recheck after remote metadata work. The atomic admission authority receives the actual current
        // user/membership snapshot, never a caller-supplied role list or an instruction to rebind an owner.
        if (!await CurrentPermission(context, DefaultIdentityPermissionKeys.IdentityLinksManage)) return;
        var latest = await registry.ReadAsync(context.RequestAborted);
        if (latest.Revision != request.ExpectedCatalogRevision || latest.Connections.SingleOrDefault(view => view.Record.Id == request.ConnectionRecordId)?.MaterialRevision != connection.MaterialRevision)
        { await ManagementFailure(context, "revision-conflict", 409); return; }
        var current = await revisions.FindWithRevisionAsync(Tenant(context), userId, context.RequestAborted);
        var currentMembership = await services.GetRequiredService<ITenantMembershipStore>().FindAsync(Tenant(context), userId, context.RequestAborted);
        if (current is null || current.Revision != request.ExpectedUserRevision) { await ManagementFailure(context, "revision-conflict", 409); return; }
        if (!await LinkAttachmentPermitted(context, services, current.Record, currentMembership)) { await ManagementFailure(context, "link-delegation-forbidden", 403); return; }
        var result = await links.AdmitAsync(new(new(Tenant(context), connection.Record.Key, tested.Issuer, request.Subject), current.Record,
            false, ExternalIdentityLinkPolicy.Admin) { ExpectedMembership = currentMembership }, context.RequestAborted);
        var attached = result.Admitted && result.UserId == userId;
        await SecurityNotification(context, "link.attach", attached ? AuthEventOutcome.Success : AuthEventOutcome.Denied, userId,
            attached ? "attached" : "link-conflict", category: AuthEventCategory.IdentityChange);
        await ManagementJson(context, new ManagedLinkAttachResponse(attached, attached ? null : "link-conflict"),
            ExternalAuthenticationApiJsonContext.Default.ManagedLinkAttachResponse, attached ? 200 : 409);
    }

    private static async ValueTask<bool> LinkAttachmentPermitted(HttpContext context, IServiceProvider services, UserRecord user, TenantMembershipRecord? membership)
    {
        if (user.Status != UserStatus.Active || membership is { Status: not TenantMembershipStatus.Active }) return false;
        var ownership = await services.GetRequiredService<IOwnershipModeProvider>().GetAsync(Tenant(context), context.RequestAborted);
        if (!services.GetRequiredService<IEffectiveCapabilitiesResolver>().Resolve(ownership,
            services.GetRequiredService<Microsoft.Extensions.Options.IOptions<FoundationIdentityOptions>>().Value.ProviderCapabilities).CanManageUsers ||
            !services.GetRequiredService<ITokenSubjectResolver>().TryResolve(context.User, out var actor) || actor.CanonicalUserId is null) return false;
        var permissions = services.GetRequiredService<ICurrentSubjectPermissionAuthorizer>();
        foreach (var permission in new[] { DefaultIdentityPermissionKeys.IdentityLinksManage, DefaultIdentityPermissionKeys.IdentityUsersRead })
            if ((await permissions.AuthorizeAsync(new(Tenant(context), actor.CanonicalUserId, permission), context.RequestAborted)).Status != CurrentSubjectPermissionStatus.Granted) return false;
        var roles = services.GetRequiredService<IRoleStore>();
        foreach (var id in user.RoleIds.Concat(membership?.RoleIds ?? new HashSet<string>()).Distinct(StringComparer.Ordinal))
        {
            var role = await roles.FindAsync(Tenant(context), id, context.RequestAborted);
            if (role is null || !await RolePermitted(context, services, role)) return false;
        }
        foreach (var permission in user.DirectPermissions.Concat(membership?.DirectPermissions ?? new HashSet<string>()).Distinct(StringComparer.Ordinal))
            if ((await permissions.AuthorizeAsync(new(Tenant(context), actor.CanonicalUserId, permission), context.RequestAborted)).Status != CurrentSubjectPermissionStatus.Granted) return false;
        return services.GetService<IExternalIdentityStore>() is { SupportsAtomicAdmission: true, SupportsQualifiedKeys: true };
    }

    private static async Task RevokeSession(HttpContext context)
    {
        if (!await CurrentPermission(context, DefaultIdentityPermissionKeys.IdentitySessionsManage) ||
            !await CurrentPermission(context, DefaultIdentityPermissionKeys.IdentityUsersRead) || !await ManagementPost(context)) return;
        var userId = Route(context, "userId"); var id = Route(context, "sessionId");
        var request = await context.Request.ReadFromJsonAsync(ExternalAuthenticationApiJsonContext.Default.ManagedSessionRevokeRequest, context.RequestAborted);
        if (!IdentityText(userId) || !SessionHex(id) || request is null || !SessionHex(request.ExpectedRevision))
        { await ManagementFailure(context, "invalid-request", 400); return; }
        await using var scope = ManagementScope(context); var services = scope.ServiceProvider;
        if (!await SessionRevocationPermitted(context, services)) { await ManagementFailure(context, "session-revocation-not-supported", 403); return; }
        var store = services.GetService<IAuthenticationSessionStore>();
        if (store?.IsDurable != true) { await ManagementFailure(context, "session-administration-unavailable", 503); return; }
        try
        {
            var found = await store.FindAsync(Tenant(context), id, context.RequestAborted);
            if (found is null || found.TenantId != Tenant(context) || found.UserId != userId) { await ManagementFailure(context, "session-not-found", 404); return; }
            var result = await store.RevokeAsync(Tenant(context), id, request.ExpectedRevision, services.GetRequiredService<TimeProvider>().GetUtcNow(), context.RequestAborted);
            var success = result is AuthenticationSessionRevocationStatus.Revoked or AuthenticationSessionRevocationStatus.AlreadyRevoked;
            await SecurityNotification(context, "session.revoke", success ? AuthEventOutcome.Success : AuthEventOutcome.Denied,
                userId, result.ToString(), id);
            await ManagementJson(context, new ManagedSessionRevokeResponse(success, result == AuthenticationSessionRevocationStatus.AlreadyRevoked,
                success ? null : result == AuthenticationSessionRevocationStatus.Conflict ? "revision-conflict" : "session-not-found"),
                ExternalAuthenticationApiJsonContext.Default.ManagedSessionRevokeResponse,
                success ? 200 : result == AuthenticationSessionRevocationStatus.Conflict ? 409 : 404);
        }
        catch (Exception exception) when (!(exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested))
        {
            await SecurityNotification(context, "session.revoke", AuthEventOutcome.Failure, userId, "storage-unavailable", id);
            await ManagementJson(context, new ManagedSessionRevokeResponse(false, Failure: "session-revocation-failed"),
                ExternalAuthenticationApiJsonContext.Default.ManagedSessionRevokeResponse, 503);
        }
    }

    private static async ValueTask<bool> SessionRevocationPermitted(HttpContext context, IServiceProvider services)
    {
        var ownership = await services.GetRequiredService<IOwnershipModeProvider>().GetAsync(Tenant(context), context.RequestAborted);
        var capabilities = services.GetRequiredService<IEffectiveCapabilitiesResolver>().Resolve(ownership,
            services.GetRequiredService<Microsoft.Extensions.Options.IOptions<FoundationIdentityOptions>>().Value.ProviderCapabilities);
        if (!capabilities.CanRevokeTokens || !services.GetRequiredService<ITokenSubjectResolver>().TryResolve(context.User, out var actor) || actor.CanonicalUserId is null) return false;
        foreach (var permission in new[] { DefaultIdentityPermissionKeys.IdentitySessionsManage, DefaultIdentityPermissionKeys.IdentityUsersRead })
            if ((await services.GetRequiredService<ICurrentSubjectPermissionAuthorizer>().AuthorizeAsync(new(Tenant(context), actor.CanonicalUserId, permission),
                context.RequestAborted)).Status != CurrentSubjectPermissionStatus.Granted) return false;
        return true;
    }

    private static async Task SecurityNotification(HttpContext context, string action, AuthEventOutcome outcome, string target, string result, string? sessionId = null,
        AuthEventCategory category = AuthEventCategory.CredentialLifecycle)
    {
        context.Items[typeof(AuthEvent)] = true;
        var actor = context.RequestServices.GetRequiredService<ITokenSubjectResolver>().TryResolve(context.User, out var subject)
            ? subject.CanonicalUserId ?? "external" : "anonymous";
        var detail = new Dictionary<string, string> { ["action"] = action, ["result"] = result };
        if (sessionId is not null) detail["sessionId"] = sessionId;
        var notice = new AuthEvent(Guid.NewGuid().ToString("n"), context.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow(),
            category, actor, target, outcome, Tenant(context), detail);
        foreach (var sink in context.RequestServices.GetServices<IAuthEventSink>())
        {
            try { await sink.EmitAsync(notice, context.RequestAborted); }
            catch (Exception exception) when (!(exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested))
            {
                // A notification fault cannot undo committed revocation or expose an upstream/sink exception.
                context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(ExternalAuthenticationApi))
                    .LogWarning("Authentication notification failed ({FailureType}).", exception.GetType().Name);
            }
        }
    }

    private static bool SessionHex(string? value) => value is { Length: 64 } && value.All(character => character is >= '0' and <= '9' or >= 'a' and <= 'f');
    private static bool IdentityText(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 400 && !value.Any(char.IsControl);
}
