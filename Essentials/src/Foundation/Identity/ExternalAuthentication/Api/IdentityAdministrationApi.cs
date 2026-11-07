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
using MinimalEndpoints;

namespace Elsa.Foundation.Identity.ExternalAuthentication.Api;

public static partial class ExternalAuthenticationApi
{
    private static void MapIdentityAdministration(IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapEndpointGroup(typeof(ExternalAuthenticationApiFeature).Assembly.GetName().Name!, ExternalAuthenticationApiJsonContext.Default, tag: "Identity");
        api.MapUnboundOperation("GET", ManagementPath + "/users", "ExternalIdentityUsers", typeof(ManagedIdentityUsersResponse), 200, null,
                context => Run(context, InspectUsers, 65536), name: "ExternalIdentityUsers", containFailures: false)
            .DisableCookieRedirect().RequirePermission(DefaultIdentityPermissionKeys.IdentityUsersRead);
        Map("GET", "/users/{userId}/sessions", "Sessions", typeof(ManagedSessionPage), InspectSessions,
            DefaultIdentityPermissionKeys.IdentitySessionsRead, DefaultIdentityPermissionKeys.IdentityUsersRead);
        Map("GET", "/users/{userId}/links", "Links", typeof(ManagedIdentityLinkPage), InspectLinks,
            DefaultIdentityPermissionKeys.IdentityLinksRead, DefaultIdentityPermissionKeys.IdentityUsersRead);
        Map("POST", "/users/{userId}/sessions/{sessionId}/revoke", "RevokeSession", typeof(ManagedSessionRevokeResponse), RevokeSession,
            DefaultIdentityPermissionKeys.IdentitySessionsManage, DefaultIdentityPermissionKeys.IdentityUsersRead, typeof(ManagedSessionRevokeRequest));
        Map("POST", "/users/{userId}/links/attach", "AttachLink", typeof(ManagedLinkAttachResponse), AttachLink,
            DefaultIdentityPermissionKeys.IdentityLinksManage, DefaultIdentityPermissionKeys.IdentityUsersRead, typeof(ManagedLinkAttachRequest));
        Map("POST", "/users/{userId}/links/remove", "RemoveLink", typeof(ManagedLinkRemoveResponse), RemoveLink,
            DefaultIdentityPermissionKeys.IdentityLinksManage, DefaultIdentityPermissionKeys.IdentityUsersRead, typeof(ManagedLinkRemoveRequest));

        void Map(string method, string suffix, string name, Type response, Func<HttpContext, Task> handler,
            string permission, string otherPermission, Type? request = null)
        {
            var endpoint = api.MapUnboundOperation(method, ManagementPath + suffix, "ExternalIdentity" + name, response, 200, null,
                    context => Run(context, handler, 65536), name: "ExternalIdentity" + name, containFailures: false)
                .DisableCookieRedirect().RequireAllPermissions(permission, otherPermission);
            if (request is not null) endpoint.WithMetadata(new AcceptsMetadata(["application/json"], request, false));
        }
    }

    private static async Task InspectUsers(HttpContext context)
    {
        if (!await CurrentPermission(context, DefaultIdentityPermissionKeys.IdentityUsersRead)) return;
        var page = Page(context); var search = context.Request.Query["search"];
        if (page is null || search.Count > 1 || search.ToString().Length > 400) { await ManagementFailure(context, "invalid-page", 400); return; }
        await using var scope = ManagementScope(context); var services = scope.ServiceProvider;
        var store = services.GetService<IPagedUserStore>();
        if (store is null) { await ManagementFailure(context, "lookup-unavailable", 503); return; }
        var users = await store.ListPageAsync(Tenant(context), page, search.FirstOrDefault(), context.RequestAborted);
        if (users.Items.Count > page.Take || users.Items.Any(item => item.TenantId != Tenant(context)))
        { await ManagementFailure(context, "lookup-unavailable", 503); return; }
        var ownership = await services.GetRequiredService<IOwnershipModeProvider>().GetAsync(Tenant(context), context.RequestAborted);
        var capabilities = services.GetRequiredService<IEffectiveCapabilitiesResolver>().Resolve(ownership,
            services.GetRequiredService<Microsoft.Extensions.Options.IOptions<FoundationIdentityOptions>>().Value.ProviderCapabilities);
        var subject = services.GetRequiredService<ITokenSubjectResolver>().TryResolve(context.User, out var found) ? found.CanonicalUserId : null;
        var permissions = services.GetRequiredService<ICurrentSubjectPermissionAuthorizer>();
        async ValueTask<bool> Granted(string permission) => subject is not null &&
            (await permissions.AuthorizeAsync(new(Tenant(context), subject, permission), context.RequestAborted)).Status == CurrentSubjectPermissionStatus.Granted;
        var sessionsAvailable = services.GetService<IAuthenticationSessionStore>()?.IsDurable == true &&
            services.GetService<ICanonicalUserSignInService>()?.SupportsDurableSessions == true;
        await ManagementJson(context, new ManagedIdentityUsersResponse(Tenant(context),
            context.RequestServices.GetRequiredService<Microsoft.Extensions.Options.IOptions<ExternalAuthenticationOptions>>().Value.CallbackPath.TrimEnd('/'),
            ownership.Mode.ToString(), capabilities.CanManageUsers, capabilities.CanRevokeTokens,
            await Granted(DefaultIdentityPermissionKeys.IdentityLinksRead), await Granted(DefaultIdentityPermissionKeys.IdentitySessionsRead),
            LinkAdministrationAvailable(context, services) ? null : "link-administration-unavailable",
            sessionsAvailable ? null : "session-administration-unavailable",
            users.Items.Select(item => new ManagedUserLookup(item.Id, item.UserName, item.DisplayName, item.Status)).ToArray(), users.TotalCount),
            ExternalAuthenticationApiJsonContext.Default.ManagedIdentityUsersResponse);
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
        var canAttach = user is not null && await LinkAttachmentPermitted(context, services, userId);
        if (canAttach && registry.DatabaseManagement)
            canAttach = context.RequestServices.GetRequiredService<IConnectionCatalogStore>().PrepareProviderWrite(
                new(registry.Connections.Where(view => view.Source == ConnectionSource.Database).Select(view => view.Record).ToArray(), registry.Revision)) is not null;
        var choices = canAttach ? registry.Connections.Where(view => view.Record.ArchivedAt is null)
            .Select(view => new ManagedLinkConnectionChoice(view.Record.Id, view.Record.Key, view.Record.DisplayName, view.Effective, view.Record.Enabled)).ToArray() : [];
        items = items.Select(item => item with { CanRemove = canAttach && item.Qualified && item.Revision is not null }).ToList();
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
        if (revisions is null || links?.SupportsQualifiedKeys != true || !LinkAdministrationAvailable(context, services))
        { await ManagementFailure(context, "link-administration-unavailable", 503); return; }
        var user = await revisions.FindWithRevisionAsync(Tenant(context), userId, context.RequestAborted);
        if (user is null) { await ManagementFailure(context, "user-not-found", 404); return; }
        if (user.Revision != request.ExpectedUserRevision) { await ManagementFailure(context, "revision-conflict", 409); return; }
        if (!await LinkAttachmentPermitted(context, services, userId)) { await ManagementFailure(context, "link-delegation-forbidden", 403); return; }
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
        if (current is null || current.Revision != request.ExpectedUserRevision) { await ManagementFailure(context, "revision-conflict", 409); return; }
        if (!await LinkAttachmentPermitted(context, services, userId)) { await ManagementFailure(context, "link-delegation-forbidden", 403); return; }
        var authorization = await services.GetRequiredService<IExternalLinkAuthorizer>().CaptureAsync(context.User, userId, request.ExpectedUserRevision, context.RequestAborted);
        if (authorization is null) { await ManagementFailure(context, "identity-authority-changed", 409); return; }
        ProviderConfigurationWrite? providerWrite = null;
        if (latest.DatabaseManagement)
        {
            var catalogs = context.RequestServices.GetRequiredService<IConnectionCatalogStore>();
            var snapshot = await catalogs.ReadAsync(context.RequestAborted);
            if (snapshot.Revision != request.ExpectedCatalogRevision) { await ManagementFailure(context, "revision-conflict", 409); return; }
            providerWrite = catalogs.PrepareProviderWrite(snapshot);
            if (providerWrite is null) { await ManagementFailure(context, "link-administration-unavailable", 503); return; }
        }
        await using var commit = context.RequestServices.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
        commit.ServiceProvider.GetRequiredService<Elsa.Workflows.Runtime.Core.Contracts.IPersistenceAccessContextBinder>().Bind(providerWrite is null
            ? Elsa.Workflows.Runtime.Core.Models.PersistenceAccessContext.Scoped(new(Tenant(context)))
            : Elsa.Workflows.Runtime.Core.Models.PersistenceAccessContext.PrivilegedGlobal(new("external-identity-administration")));
        var administration = commit.ServiceProvider.GetService<IExternalIdentityAdministrationStore>();
        if (administration?.SupportsIdentityRevisionGuards != true || providerWrite is not null && !administration.SupportsProviderRevisionGuards)
        { await ManagementFailure(context, "link-administration-unavailable", 503); return; }
        ExternalUserAdmissionResult result;
        try
        {
            result = await administration.AttachAsync(new(new(Tenant(context), connection.Record.Key, tested.Issuer, request.Subject), authorization.User,
                false, ExternalIdentityLinkPolicy.Admin) { ExpectedMembership = authorization.Membership }, authorization.Owner, authorization.Guards,
                providerWrite, context.RequestAborted);
        }
        catch (Exception exception) when (!(exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested))
        {
            await SecurityNotification(context, "link.attach", AuthEventOutcome.Failure, userId, "storage-outcome-unavailable", category: AuthEventCategory.IdentityChange);
            await ManagementFailure(context, "link-attachment-unavailable", 503); return;
        }
        var attached = result.Admitted && result.UserId == authorization.User.Id;
        await SecurityNotification(context, "link.attach", attached ? AuthEventOutcome.Success : AuthEventOutcome.Denied, userId,
            attached ? "attached" : result.Failure ?? "link-conflict", category: AuthEventCategory.IdentityChange);
        await ManagementJson(context, new ManagedLinkAttachResponse(attached, attached ? null : result.Failure ?? "link-conflict"),
            ExternalAuthenticationApiJsonContext.Default.ManagedLinkAttachResponse, attached ? 200 : 409);
    }

    private static async Task RemoveLink(HttpContext context)
    {
        if (!await CurrentPermission(context, DefaultIdentityPermissionKeys.IdentityLinksManage) ||
            !await CurrentPermission(context, DefaultIdentityPermissionKeys.IdentityUsersRead) || !await ManagementPost(context)) return;
        var userId = Route(context, "userId");
        var request = await context.Request.ReadFromJsonAsync(ExternalAuthenticationApiJsonContext.Default.ManagedLinkRemoveRequest, context.RequestAborted);
        if (!IdentityText(userId) || request is null || !IdentityText(request.Provider) || !IdentityText(request.Issuer) || !IdentityText(request.Subject) ||
            !IdentityText(request.ExpectedUserRevision) || !IdentityText(request.ExpectedLinkRevision))
        { await ManagementFailure(context, "invalid-request", 400); return; }
        await using var scope = ManagementScope(context); var services = scope.ServiceProvider;
        if (!LinkAdministrationAvailable(context, services) || services.GetService<IRevisionAwareExternalIdentityStore>() is not { SupportsQualifiedKeys: true } links ||
            services.GetService<IRevisionAwareUserStore>() is not { } users)
        { await ManagementFailure(context, "link-administration-unavailable", 503); return; }
        var user = await users.FindWithRevisionAsync(Tenant(context), userId, context.RequestAborted);
        if (user is null) { await ManagementFailure(context, "user-not-found", 404); return; }
        if (user.Revision != request.ExpectedUserRevision) { await ManagementFailure(context, "revision-conflict", 409); return; }
        var key = new ExternalIdentityKey(Tenant(context), request.Provider, request.Issuer, request.Subject);
        var link = await links.FindByKeyWithRevisionAsync(key, context.RequestAborted);
        if (link?.Record.UserId != user.Record.Id) { await ManagementFailure(context, "link-not-found", 404); return; }
        if (link.Revision != request.ExpectedLinkRevision) { await ManagementFailure(context, "revision-conflict", 409); return; }
        var registry = context.RequestServices.GetRequiredService<IConnectionRegistry>();
        var before = await registry.ReadAsync(context.RequestAborted);
        if (before.Revision != request.ExpectedCatalogRevision) { await ManagementFailure(context, "revision-conflict", 409); return; }
        var authorization = await services.GetRequiredService<IExternalLinkAuthorizer>().CaptureAsync(context.User, userId, request.ExpectedUserRevision, context.RequestAborted);
        if (authorization is null) { await ManagementFailure(context, "link-delegation-forbidden", 403); return; }
        IReadOnlyList<IamRevisionGuard>? guards;
        try { guards = await services.GetRequiredService<IExternalLinkRecovery>().CaptureAsync(context.User, authorization, key, context.RequestAborted); }
        catch (OperationCanceledException) when (!context.RequestAborted.IsCancellationRequested)
        { await ManagementFailure(context, "provider-test-timeout", 504); return; }
        if (guards is null) { await ManagementFailure(context, "final-login-path", 409); return; }
        var latest = await registry.ReadAsync(context.RequestAborted);
        if (latest.Revision != request.ExpectedCatalogRevision) { await ManagementFailure(context, "revision-conflict", 409); return; }
        ProviderConfigurationWrite? providerWrite = null;
        if (latest.DatabaseManagement)
        {
            var catalogs = context.RequestServices.GetRequiredService<IConnectionCatalogStore>();
            var snapshot = await catalogs.ReadAsync(context.RequestAborted);
            if (snapshot.Revision != request.ExpectedCatalogRevision) { await ManagementFailure(context, "revision-conflict", 409); return; }
            providerWrite = catalogs.PrepareProviderWrite(snapshot);
            if (providerWrite is null) { await ManagementFailure(context, "link-administration-unavailable", 503); return; }
        }
        await using var commit = context.RequestServices.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
        commit.ServiceProvider.GetRequiredService<Elsa.Workflows.Runtime.Core.Contracts.IPersistenceAccessContextBinder>().Bind(providerWrite is null
            ? Elsa.Workflows.Runtime.Core.Models.PersistenceAccessContext.Scoped(new(Tenant(context)))
            : Elsa.Workflows.Runtime.Core.Models.PersistenceAccessContext.PrivilegedGlobal(new("external-identity-administration")));
        var administration = commit.ServiceProvider.GetService<IExternalIdentityAdministrationStore>();
        if (administration?.SupportsIdentityRevisionGuards != true || providerWrite is not null && !administration.SupportsProviderRevisionGuards)
        { await ManagementFailure(context, "link-administration-unavailable", 503); return; }
        ExternalIdentityRemovalResult result;
        try { result = await administration.RemoveAsync(key, authorization.Owner, request.ExpectedLinkRevision, guards, providerWrite, context.RequestAborted); }
        catch (Exception exception) when (!(exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested))
        {
            await SecurityNotification(context, "link.remove", AuthEventOutcome.Failure, authorization.User.Id, "storage-outcome-unavailable", category: AuthEventCategory.IdentityChange);
            await ManagementFailure(context, "link-removal-unavailable", 503); return;
        }
        var removed = result.Status == ExternalIdentityRemovalStatus.Removed;
        await SecurityNotification(context, "link.remove", removed ? AuthEventOutcome.Success : AuthEventOutcome.Denied, authorization.User.Id,
            removed ? "removed" : result.Status.ToString(), category: AuthEventCategory.IdentityChange);
        await ManagementJson(context, new ManagedLinkRemoveResponse(removed, removed ? null : result.Status == ExternalIdentityRemovalStatus.Missing ? "link-not-found" : "revision-conflict"),
            ExternalAuthenticationApiJsonContext.Default.ManagedLinkRemoveResponse, removed ? 200 : result.Status == ExternalIdentityRemovalStatus.Missing ? 404 : 409);
    }

    private static bool LinkAdministrationAvailable(HttpContext context, IServiceProvider services)
    {
        var administration = services.GetService<IExternalIdentityAdministrationStore>();
        return administration?.SupportsIdentityRevisionGuards == true &&
            (!context.RequestServices.GetRequiredService<Microsoft.Extensions.Options.IOptions<ExternalAuthenticationOptions>>().Value.AllowDatabaseManagement ||
                administration.SupportsProviderRevisionGuards);
    }

    private static async ValueTask<bool> LinkAttachmentPermitted(HttpContext context, IServiceProvider services, string userId) =>
        LinkAdministrationAvailable(context, services) &&
        await services.GetRequiredService<IExternalLinkAuthorizer>().CaptureAsync(context.User, userId, cancellationToken: context.RequestAborted) is not null;

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
