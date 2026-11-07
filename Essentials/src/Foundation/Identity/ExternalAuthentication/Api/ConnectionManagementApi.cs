using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Foundation.Identity.Core.Authentication;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.Core.Ownership;
using Elsa.Foundation.Identity.Core.Security;
using Elsa.Foundation.Identity.ExternalAuthentication.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MinimalEndpoints;

namespace Elsa.Foundation.Identity.ExternalAuthentication.Api;

public static partial class ExternalAuthenticationApi
{
    private const string ManagementPath = "/_elsa/identity/external-connections";
    private static readonly HashSet<string> OidcSettingNames = ["authority", "clientId", "scopes", "clientAuthentication", "signingAlgorithm"];

    private static void MapManagement(IEndpointRouteBuilder endpoints)
    {
        var api = endpoints.MapEndpointGroup(typeof(ExternalAuthenticationApiFeature).Assembly.GetName().Name!, ExternalAuthenticationApiJsonContext.Default, tag: "Identity");
        Map("GET", "", "List", typeof(ManagedConnectionCatalogResponse), ListConnections, DefaultIdentityPermissionKeys.IdentityProvidersRead);
        Map("GET", "/{id}", "Detail", typeof(ManagedConnectionDetailResponse), ConnectionDetail, DefaultIdentityPermissionKeys.IdentityProvidersRead);
        Map("POST", "/assess", "Assess", typeof(ManagedConnectionAssessmentResponse), context => ChangeConnection(context, false), DefaultIdentityPermissionKeys.IdentityProvidersManage, typeof(ConnectionWrite));
        Map("POST", "/commands", "Change", typeof(ConnectionWriteResult), context => ChangeConnection(context, true), DefaultIdentityPermissionKeys.IdentityProvidersManage, typeof(ConnectionWrite));
        Map("POST", "/test", "Test", typeof(ManagedConnectionTestResponse), TestConnection, DefaultIdentityPermissionKeys.IdentityProvidersManage, typeof(ManagedConnectionTestRequest));
        Map("GET", "/lookups/users", "Users", typeof(ManagedUserPage), LookupUsers, DefaultIdentityPermissionKeys.IdentityUsersRead);
        Map("GET", "/lookups/roles", "Roles", typeof(ManagedRolePage), LookupRoles, DefaultIdentityPermissionKeys.IdentityRolesRead);
        void Map(string method, string suffix, string operation, Type response, Func<HttpContext, Task> handler, string permission, Type? request = null)
        {
            var endpoint = api.MapUnboundOperation(method, ManagementPath + suffix, "ExternalConnections" + operation, response, 200, null,
                context => Run(context, handler, 65536), name: "ExternalConnections" + operation, containFailures: false).DisableCookieRedirect();
            if (operation is "Users" or "Roles") endpoint.RequireAllPermissions(permission, DefaultIdentityPermissionKeys.IdentityProvidersManage);
            else endpoint.RequirePermission(permission);
            if (request is not null) endpoint.WithMetadata(new AcceptsMetadata(["application/json"], request, false));
        }
    }

    private static async Task ListConnections(HttpContext context)
    {
        if (!await CurrentPermission(context, DefaultIdentityPermissionKeys.IdentityProvidersRead)) return;
        var registry = await context.RequestServices.GetRequiredService<IConnectionRegistry>().ReadAsync(context.RequestAborted);
        await ManagementJson(context, await CatalogResponse(context, registry), ExternalAuthenticationApiJsonContext.Default.ManagedConnectionCatalogResponse);
    }

    private static async Task ConnectionDetail(HttpContext context)
    {
        if (!await CurrentPermission(context, DefaultIdentityPermissionKeys.IdentityProvidersRead)) return;
        var registry = await context.RequestServices.GetRequiredService<IConnectionRegistry>().ReadAsync(context.RequestAborted);
        var view = registry.Connections.SingleOrDefault(view => view.Record.Id == Route(context, "id"));
        if (view is null) { await ManagementFailure(context, "connection-not-found", 404); return; }
        var readiness = await context.RequestServices.GetRequiredService<IConnectionReadiness>().AssessAsync(view.Record, context.RequestAborted);
        await ManagementJson(context, new ManagedConnectionDetailResponse(registry.Revision, Projection(context, view) with { Readiness = readiness }),
            ExternalAuthenticationApiJsonContext.Default.ManagedConnectionDetailResponse);
    }

    private static async Task ChangeConnection(HttpContext context, bool apply)
    {
        if (!await CurrentPermission(context, DefaultIdentityPermissionKeys.IdentityProvidersManage) || !await ManagementPost(context)) return;
        var request = await context.Request.ReadFromJsonAsync(ExternalAuthenticationApiJsonContext.Default.ConnectionWrite, context.RequestAborted);
        if (request?.Record is null || !Enum.IsDefined(request.Change) || request.Record.Admission?.DefaultRoleIds is null ||
            request.Record.Admission.DefaultRoleIds.Count > 256 || request.Record.Admission.DefaultRoleIds.Any(id => string.IsNullOrWhiteSpace(id) || id.Length > 400) ||
            (request.Change is ConnectionChange.Create or ConnectionChange.CreateOverride or ConnectionChange.Update or ConnectionChange.PromoteOverride) && !Editable(request.Record))
        { await ManagementFailure(context, "invalid-configuration", 400); return; }
        var current = await context.RequestServices.GetRequiredService<IConnectionRegistry>().ReadAsync(context.RequestAborted);
        if (!current.DatabaseManagement) { await ManagementFailure(context, "database-management-disabled", 403); return; }
        if (current.Revision != request.ExpectedRevision) { await ManagementFailure(context, "revision-conflict", 409); return; }
        var policyRecord = request.Change is ConnectionChange.Enable or ConnectionChange.Restore
            ? current.Connections.SingleOrDefault(view => view.Source == ConnectionSource.Database && view.Record.Id == request.Record.Id)?.Record
            : request.Record;
        if (policyRecord is null) { await ManagementFailure(context, "database-record-not-found", 404); return; }
        if (!await DelegateRoles(context, request.Change, policyRecord)) { await ManagementFailure(context, "role-delegation-forbidden", 403); return; }
        var writer = context.RequestServices.GetRequiredService<IConnectionWriter>();
        if (apply)
        {
            var result = await writer.ApplyAsync(request, context.User, context.RequestAborted);
            await SecurityNotification(context, "connection." + request.Change.ToString().ToLowerInvariant(),
                result.Saved ? AuthEventOutcome.Success : AuthEventOutcome.Denied, "external-connections", result.Saved ? "saved" : "refused",
                category: AuthEventCategory.IdentityChange);
            await ManagementJson(context, result, ExternalAuthenticationApiJsonContext.Default.ConnectionWriteResult, result.Saved ? 200 : FailureStatus(result.Failure));
        }
        else
        {
            var assessed = await writer.AssessAsync(request, context.User, context.RequestAborted);
            await ManagementJson(context, new ManagedConnectionAssessmentResponse(assessed.CanApply, assessed.Failure,
                assessed.Proposed is null ? null : await CatalogResponse(context, assessed.Proposed)),
                ExternalAuthenticationApiJsonContext.Default.ManagedConnectionAssessmentResponse, assessed.CanApply ? 200 : FailureStatus(assessed.Failure));
        }
    }

    private static async Task TestConnection(HttpContext context)
    {
        if (!await CurrentPermission(context, DefaultIdentityPermissionKeys.IdentityProvidersManage) || !await ManagementPost(context)) return;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        try { await TestSavedConnection(context, deadline.Token); }
        catch (OperationCanceledException) when (!context.RequestAborted.IsCancellationRequested)
        { await ManagementFailure(context, "provider-test-timeout", 504); }
    }

    private static async Task TestSavedConnection(HttpContext context, CancellationToken token)
    {
        var request = await context.Request.ReadFromJsonAsync(ExternalAuthenticationApiJsonContext.Default.ManagedConnectionTestRequest, token);
        if (request is null || string.IsNullOrWhiteSpace(request.ConnectionRecordId) || request.ConnectionRecordId.Length > 128)
        { await ManagementFailure(context, "invalid-request", 400); return; }
        var registry = context.RequestServices.GetRequiredService<IConnectionRegistry>();
        var before = await registry.ReadAsync(token);
        if (before.Revision != request.ExpectedRevision) { await ManagementFailure(context, "revision-conflict", 409); return; }
        var view = before.Connections.SingleOrDefault(view => view.Record.Id == request.ConnectionRecordId && view.Record.ArchivedAt is null);
        if (view is null) { await ManagementFailure(context, "connection-not-found", 404); return; }
        var readiness = await context.RequestServices.GetRequiredService<IConnectionReadiness>().AssessAsync(view.Record, token);
        var failure = readiness.ProtocolFailure;
        if (readiness.Ready)
        {
            var protocols = context.RequestServices.GetServices<IExternalAuthenticationProtocol>().Where(protocol => protocol.Name == view.Record.Protocol).Take(2).ToArray();
            failure = protocols.Length == 1 ? (await protocols[0].TestAsync(view.Record, token)).Failure : ExternalProtocolFailure.InvalidConfiguration;
        }
        var after = await registry.ReadAsync(token);
        var current = after.Connections.SingleOrDefault(item => item.Record.Id == view.Record.Id);
        var result = new ManagedConnectionTestResponse(view.Record.Id, view.MaterialRevision,
            context.RequestServices.GetRequiredService<TimeProvider>().GetUtcNow(), readiness, failure,
            before.Revision != after.Revision || current?.MaterialRevision != view.MaterialRevision);
        // Never serialize the protocol check: its issuer/credential binding is private recovery evidence.
        await ManagementJson(context, result, ExternalAuthenticationApiJsonContext.Default.ManagedConnectionTestResponse);
    }

    private static async Task LookupUsers(HttpContext context)
    {
        if (!await CurrentPermission(context, DefaultIdentityPermissionKeys.IdentityUsersRead) ||
            !await CurrentPermission(context, DefaultIdentityPermissionKeys.IdentityProvidersManage)) return;
        var page = Page(context);
        var search = context.Request.Query["search"];
        if (page is null || search.Count > 1 || search.ToString().Length > 400) { await ManagementFailure(context, "invalid-page", 400); return; }
        await using var scope = ManagementScope(context);
        var store = scope.ServiceProvider.GetService<IPagedUserStore>();
        if (store is null) { await ManagementFailure(context, "lookup-unavailable", 503); return; }
        var result = await store.ListPageAsync(Tenant(context), page, search.FirstOrDefault(), context.RequestAborted);
        await ManagementJson(context, new ManagedUserPage(result.Items.Select(user => new ManagedUserLookup(user.Id, user.UserName, user.DisplayName, user.Status)).ToArray(), result.TotalCount),
            ExternalAuthenticationApiJsonContext.Default.ManagedUserPage);
    }

    private static async Task LookupRoles(HttpContext context)
    {
        if (!await CurrentPermission(context, DefaultIdentityPermissionKeys.IdentityRolesRead) ||
            !await CurrentPermission(context, DefaultIdentityPermissionKeys.IdentityProvidersManage)) return;
        var page = Page(context);
        if (page is null) { await ManagementFailure(context, "invalid-page", 400); return; }
        await using var scope = ManagementScope(context);
        var store = scope.ServiceProvider.GetService<IPagedRoleStore>();
        if (store is null) { await ManagementFailure(context, "lookup-unavailable", 503); return; }
        var result = await store.ListPageAsync(Tenant(context), page, context.RequestAborted);
        var roles = new List<ManagedRoleLookup>();
        foreach (var role in result.Items) roles.Add(new(role.Id, role.Name, await RolePermitted(context, scope.ServiceProvider, role)));
        await ManagementJson(context, new ManagedRolePage(roles, result.TotalCount), ExternalAuthenticationApiJsonContext.Default.ManagedRolePage);
    }

    private static async Task<bool> DelegateRoles(HttpContext context, ConnectionChange change, ConnectionRecord record)
    {
        if (change is ConnectionChange.Disable or ConnectionChange.Archive or ConnectionChange.Remove) return true;
        if (record.Admission?.DefaultRoleIds is null || record.Admission.DefaultRoleIds.Count > 256) return false;
        await using var scope = ManagementScope(context);
        var roles = scope.ServiceProvider.GetService<IRoleStore>();
        foreach (var id in record.Admission.DefaultRoleIds.Distinct(StringComparer.Ordinal))
        {
            if (roles is null) return false;
            var role = await roles.FindAsync(Tenant(context), id, context.RequestAborted);
            if (role is null || !await RolePermitted(context, scope.ServiceProvider, role)) return false;
        }
        return true;
    }

    private static async Task<bool> RolePermitted(HttpContext context, IServiceProvider services, RoleRecord role)
    {
        var allowed = context.RequestServices.GetRequiredService<IOptions<ExternalAuthenticationOptions>>().Value.AllowedRoleIds;
        if (allowed.Count != 0 && !allowed.Contains(role.Id, StringComparer.Ordinal)) return false;
        if (!services.GetRequiredService<ITokenSubjectResolver>().TryResolve(context.User, out var subject) || subject.CanonicalUserId is null) return false;
        foreach (var permission in role.Permissions)
            if ((await services.GetRequiredService<ICurrentSubjectPermissionAuthorizer>().AuthorizeAsync(
                    new(Tenant(context), subject.CanonicalUserId, permission, role), context.RequestAborted)).Status != CurrentSubjectPermissionStatus.Granted) return false;
        return true;
    }

    private static async Task<bool> CurrentPermission(HttpContext context, string permission)
    {
        if (context.User.Identity?.IsAuthenticated != true) { await ManagementFailure(context, "not-authenticated", 401); return false; }
        await using var scope = ManagementScope(context);
        var services = scope.ServiceProvider;
        if (!services.GetRequiredService<ITokenSubjectResolver>().TryResolve(context.User, out var subject) ||
            subject.TenantId != Tenant(context) || subject.CanonicalUserId is null ||
            services.GetService<ICanonicalUserSignInService>() is not { } issuer ||
            !await issuer.CanSignInAsync(new(Tenant(context), subject.CanonicalUserId), cancellationToken: context.RequestAborted) ||
            (await services.GetRequiredService<ICurrentSubjectPermissionAuthorizer>().AuthorizeAsync(
                new(Tenant(context), subject.CanonicalUserId, permission), context.RequestAborted)).Status != CurrentSubjectPermissionStatus.Granted)
        { await ManagementFailure(context, "not-authorized", 403); return false; }
        return true;
    }

    private static async Task<bool> ManagementPost(HttpContext context)
    {
        if (!context.Request.HasJsonContentType()) { await ManagementFailure(context, "json-required", 415); return false; }
        try { await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context); }
        catch (AntiforgeryValidationException) { await ManagementFailure(context, "antiforgery-required", 400); return false; }
        return true;
    }
    private static async ValueTask<ManagedConnectionCatalogResponse> CatalogResponse(HttpContext context, ConnectionRegistryView registry)
    {
        await using var scope = ManagementScope(context);
        var ownership = await scope.ServiceProvider.GetRequiredService<IOwnershipModeProvider>().GetAsync(Tenant(context), context.RequestAborted);
        var capabilities = scope.ServiceProvider.GetRequiredService<IEffectiveCapabilitiesResolver>().Resolve(ownership,
            scope.ServiceProvider.GetRequiredService<IOptions<FoundationIdentityOptions>>().Value.ProviderCapabilities) with { CanMapGroups = false };
        var canManage = scope.ServiceProvider.GetRequiredService<ITokenSubjectResolver>().TryResolve(context.User, out var subject) && subject.CanonicalUserId is not null &&
            (await scope.ServiceProvider.GetRequiredService<ICurrentSubjectPermissionAuthorizer>().AuthorizeAsync(
                new(Tenant(context), subject.CanonicalUserId, DefaultIdentityPermissionKeys.IdentityProvidersManage), context.RequestAborted)).Status == CurrentSubjectPermissionStatus.Granted;
        var coordinated = !registry.DatabaseManagement || context.RequestServices.GetRequiredService<IConnectionCatalogStore>().SupportsIdentityRevisionGuards;
        var policies = scope.ServiceProvider.GetServices<IExternalProvisioningPolicy>().Select(policy => policy.Name)
            .Where(name => !string.IsNullOrWhiteSpace(name) && name.Length <= 80 && name != "reject").GroupBy(name => name, StringComparer.Ordinal)
            .Where(group => group.Count() == 1).Select(group => group.Key).Append("reject").Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Take(256).ToArray();
        return new(Tenant(context), context.RequestServices.GetRequiredService<IOptions<ExternalAuthenticationOptions>>().Value.CallbackPath.TrimEnd('/'),
            registry.Revision, registry.DatabaseManagement, canManage && coordinated, policies, capabilities,
            registry.Connections.Select(view => Projection(context, view)).ToArray(), coordinated ? null : "identity-coordination-unavailable");
    }
    private static ManagedConnectionResponse Projection(HttpContext context, ConnectionView view)
    {
        var record = view.Record;
        var flows = context.RequestServices.GetRequiredService<IExternalAuthenticationFlowService>();
        // Unknown adapters/settings are visible as source metadata, never an arbitrary settings/secret-value dump.
        var projected = record with
        {
            Settings = record.Protocol == "oidc" ? record.Settings.Where(pair => OidcSettingNames.Contains(pair.Key)).ToDictionary() : new Dictionary<string, string>(),
            Secrets = record.Protocol == "oidc" ? record.Secrets.Where(pair => pair.Key == "clientSecret").ToDictionary() : new Dictionary<string, Elsa.Secrets.Core.Models.SecretReference>()
        };
        var editable = view.Source == ConnectionSource.Database && record.ArchivedAt is null &&
            context.RequestServices.GetRequiredService<IOptions<ExternalAuthenticationOptions>>().Value.AllowDatabaseManagement &&
            context.RequestServices.GetRequiredService<IConnectionCatalogStore>().SupportsIdentityRevisionGuards && Editable(record);
        return new(projected, view.Source, view.Effective, view.ShadowedById, view.OverridePermitted, view.MaterialRevision, editable, Editable(record),
            flows.GetCallbackUri(record.Key, AuthenticationArtifactPurpose.SignIn), flows.GetCallbackUri(record.Key, AuthenticationArtifactPurpose.Preview));
    }
    private static bool Editable(ConnectionRecord record) => record.Protocol == "oidc" && record.Settings is not null && record.Secrets is not null &&
        record.Settings.Keys.All(OidcSettingNames.Contains) && record.Secrets.Keys.All(key => key == "clientSecret");
    private static IamPageRequest? Page(HttpContext context)
    {
        var query = context.Request.Query;
        if (query["skip"].Count > 1 || query["take"].Count > 1) return null;
        var skip = 0; var take = 50;
        if (query.ContainsKey("skip") && !int.TryParse(query["skip"], NumberStyles.None, CultureInfo.InvariantCulture, out skip) ||
            query.ContainsKey("take") && !int.TryParse(query["take"], NumberStyles.None, CultureInfo.InvariantCulture, out take) ||
            skip < 0 || take is < 1 or > 100) return null;
        return new(skip, take);
    }
    private static string Tenant(HttpContext context) => context.RequestServices.GetRequiredService<IOptions<ExternalAuthenticationOptions>>().Value.TenantId;
    private static AsyncServiceScope ManagementScope(HttpContext context)
    {
        var scope = context.RequestServices.GetRequiredService<IServiceScopeFactory>().CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IPersistenceAccessContextBinder>().Bind(PersistenceAccessContext.Scoped(new PersistenceScope(Tenant(context))));
        return scope;
    }
    private static int FailureStatus(string? failure) => failure switch
    {
        "not-authorized" or "role-delegation-forbidden" or "deployment-override-forbidden" or "database-management-disabled" => 403,
        "database-record-not-found" => 404,
        "revision-conflict" or "connection-key-in-use" or "final-login-path" => 409,
        "identity-authority-changed" => 409,
        "identity-coordination-unavailable" => 503,
        _ => 400
    };
    private static Task ManagementJson<T>(HttpContext context, T response, JsonTypeInfo<T> type, int status = 200) => Results.Json(response, type, statusCode: status).ExecuteAsync(context);
    private static Task ManagementFailure(HttpContext context, string failure, int status) => ManagementJson(context,
        new ManagedConnectionError(failure), ExternalAuthenticationApiJsonContext.Default.ManagedConnectionError, status);
}
