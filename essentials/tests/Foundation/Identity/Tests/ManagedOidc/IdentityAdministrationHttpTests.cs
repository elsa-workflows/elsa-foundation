using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Foundation.Identity.Core.Authentication;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.Core.Ownership;
using Elsa.Foundation.Identity.Core.Security;
using Elsa.Foundation.Identity.Extensions;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Elsa.Foundation.Identity.Tests.ManagedOidc;

public sealed partial class ExternalAuthenticationHttpTests
{
    [Fact]
    public async Task Session_and_link_inspection_project_safe_bounded_current_metadata_and_selected_revocation_preserves_siblings()
    {
        var events = new CapturedSecurityEvents();
        await using var host = await HttpHost.Start(services => services.AddSingleton<IAuthEventSink>(events));
        using var administrator = host.Browser(); using var first = host.Browser(); using var sibling = host.Browser();
        await host.LoginAdministrator(administrator);
        foreach (var browser in new[] { first, sibling })
        { var callback = await host.StartAndAuthorize(browser); using var complete = await browser.GetAsync(callback); Assert.Equal(HttpStatusCode.Found, complete.StatusCode); }
        var user = (await Session(first)).Subject!; var pair = await Pair(first); var siblingPair = await Pair(sibling);
        var path = $"/users/{user}/sessions";
        var sessions = await AccessDocument(administrator, path);
        Assert.Equal(2, sessions.GetProperty("totalCount").GetInt64());
        Assert.Equal(2, sessions.GetProperty("items").GetArrayLength());
        var selected = sessions.GetProperty("items")[0]; var id = selected.GetProperty("id").GetString()!;
        var firstSession = new Microsoft.IdentityModel.JsonWebTokens.JsonWebToken(pair.AccessToken).GetClaim(IdentityClaimTypes.SessionId).Value;
        selected = sessions.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("id").GetString() == firstSession);
        id = selected.GetProperty("id").GetString()!;
        Assert.Equal("active", selected.GetProperty("state").GetString()); Assert.True(selected.GetProperty("canRevoke").GetBoolean());
        Assert.Equal(host.Company.Issuer, selected.GetProperty("issuer").GetString());
        Assert.DoesNotContain("securityVersion", sessions.GetRawText(), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("opaque-Subject", sessions.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("credential", sessions.GetRawText(), StringComparison.OrdinalIgnoreCase);
        var links = await AccessDocument(administrator, $"/users/{user}/links?take=1");
        var link = Assert.Single(links.GetProperty("items").EnumerateArray());
        Assert.Equal("company", link.GetProperty("provider").GetString()); Assert.Equal(host.Company.Issuer, link.GetProperty("issuer").GetString());
        Assert.Equal("opaque-Subject", link.GetProperty("subject").GetString()); Assert.True(link.GetProperty("qualified").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(link.GetProperty("revision").GetString()));
        using (var wrongUser = await ManagementPost(host, administrator, $"/users/not-the-owner/sessions/{id}/revoke", new { expectedRevision = selected.GetProperty("revision").GetString() }))
            Assert.Equal(HttpStatusCode.NotFound, wrongUser.StatusCode);
        using (var stale = await ManagementPost(host, administrator, path + $"/{id}/revoke", new { expectedRevision = new string('0', 64) }))
            Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using (var live = await Bearer(first, pair.AccessToken)) Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        using (var revoked = await ManagementPost(host, administrator, path + $"/{id}/revoke", new { expectedRevision = selected.GetProperty("revision").GetString() }))
        { Assert.Equal(HttpStatusCode.OK, revoked.StatusCode); Assert.True((await revoked.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("revoked").GetBoolean()); }
        using (var replay = await ManagementPost(host, administrator, path + $"/{id}/revoke", new { expectedRevision = selected.GetProperty("revision").GetString() }))
        { Assert.Equal(HttpStatusCode.OK, replay.StatusCode); Assert.True((await replay.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("alreadyRevoked").GetBoolean()); }
        using (var dead = await Bearer(first, pair.AccessToken)) Assert.Equal(HttpStatusCode.Unauthorized, dead.StatusCode);
        using (var dead = await first.PostAsJsonAsync("_elsa/identity/refresh", new { refreshToken = pair.RefreshToken })) Assert.Equal(HttpStatusCode.Unauthorized, dead.StatusCode);
        Assert.Equal("anonymous", (await Session(first)).Status);
        using (var live = await Bearer(sibling, siblingPair.AccessToken)) Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal("authenticated", (await Session(sibling)).Status);
        var after = await AccessDocument(administrator, path);
        var retired = after.GetProperty("items").EnumerateArray().Single(item => item.GetProperty("id").GetString() == id);
        Assert.Equal("revoked", retired.GetProperty("state").GetString()); Assert.False(retired.GetProperty("canRevoke").GetBoolean());
        Assert.Contains(events.Items, notice => notice.Category == AuthEventCategory.CredentialLifecycle && notice.Outcome == AuthEventOutcome.Success && notice.Detail["action"] == "session.revoke");
        Assert.DoesNotContain("opaque-Subject", JsonSerializer.Serialize(events.Items), StringComparison.Ordinal);
        Assert.DoesNotContain("securityVersion", JsonSerializer.Serialize(events.Items), StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("refreshToken", JsonSerializer.Serialize(events.Items), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Access_inspection_uses_dedicated_permissions_and_current_saved_grants_instead_of_old_cookie_wildcards()
    {
        await using var host = await HttpHost.Start(); using var administrator = host.Browser();
        await host.LoginAdministrator(administrator); var user = (await Session(administrator)).Subject!;
        await GrantAdministrator(host, DefaultIdentityPermissionKeys.IdentityUsersRead, DefaultIdentityPermissionKeys.IdentitySessionsRead, DefaultIdentityPermissionKeys.IdentityLinksRead);
        using (var sessions = await administrator.GetAsync(Management + $"/users/{user}/sessions"))
        {
            Assert.Equal(HttpStatusCode.OK, sessions.StatusCode);
            var json = await sessions.Content.ReadFromJsonAsync<JsonElement>();
            Assert.True(Assert.Single(json.GetProperty("items").EnumerateArray()).GetProperty("current").GetBoolean());
            Assert.False(Assert.Single(json.GetProperty("items").EnumerateArray()).GetProperty("canRevoke").GetBoolean());
        }
        using (var links = await administrator.GetAsync(Management + $"/users/{user}/links")) Assert.Equal(HttpStatusCode.OK, links.StatusCode);
        using (var catalog = await administrator.GetAsync(Management)) Assert.Equal(HttpStatusCode.Forbidden, catalog.StatusCode);
        var page = await AccessDocument(administrator, $"/users/{user}/sessions"); var item = page.GetProperty("items")[0];
        using (var denied = await ManagementPost(host, administrator, $"/users/{user}/sessions/{item.GetProperty("id").GetString()}/revoke", new { expectedRevision = item.GetProperty("revision").GetString() }))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        await GrantAdministrator(host, DefaultIdentityPermissionKeys.IdentityUsersRead, DefaultIdentityPermissionKeys.IdentityCredentialsManage);
        using (var denied = await administrator.GetAsync(Management + $"/users/{user}/sessions")) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using (var denied = await ManagementPost(host, administrator, $"/users/{user}/sessions/{item.GetProperty("id").GetString()}/revoke", new { expectedRevision = item.GetProperty("revision").GetString() }))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode); // Non-interactive credential management is a different grant.
    }

    [Theory]
    [InlineData("csrf", HttpStatusCode.BadRequest)]
    [InlineData("media", HttpStatusCode.UnsupportedMediaType)]
    [InlineData("revision", HttpStatusCode.BadRequest)]
    [InlineData("unknown", HttpStatusCode.BadRequest)]
    public async Task Session_revocation_refuses_invalid_protected_requests_without_changing_the_session(string invalid, HttpStatusCode expected)
    {
        await using var host = await HttpHost.Start(); using var administrator = host.Browser();
        await host.LoginAdministrator(administrator); var user = (await Session(administrator)).Subject!;
        var page = await AccessDocument(administrator, $"/users/{user}/sessions"); var item = page.GetProperty("items")[0];
        var id = item.GetProperty("id").GetString(); var revision = item.GetProperty("revision").GetString();
        using var request = new HttpRequestMessage(HttpMethod.Post, Management + $"/users/{user}/sessions/{id}/revoke");
        if (invalid != "csrf") request.Headers.Add("RequestVerificationToken", await host.Csrf(administrator));
        request.Content = invalid switch
        {
            "media" => new StringContent("{}", Encoding.UTF8, "text/plain"),
            "unknown" => JsonContent.Create(new { expectedRevision = revision, securityVersion = "private-canary" }),
            _ => JsonContent.Create(new { expectedRevision = invalid == "revision" ? "wrong" : revision })
        };
        using var denied = await administrator.SendAsync(request); Assert.Equal(expected, denied.StatusCode);
        Assert.Equal("authenticated", (await Session(administrator)).Status);
        Assert.Equal("active", (await AccessDocument(administrator, $"/users/{user}/sessions")).GetProperty("items")[0].GetProperty("state").GetString());
    }

    [Fact]
    public async Task Session_revocation_respects_current_ownership_even_with_an_administrative_saved_grant()
    {
        await using var host = await HttpHost.Start(); using var administrator = host.Browser(); using var target = host.Browser();
        await host.LoginAdministrator(administrator);
        var callback = await host.StartAndAuthorize(target);
        using (var complete = await target.GetAsync(callback)) Assert.Equal(HttpStatusCode.Found, complete.StatusCode);
        var user = (await Session(target)).Subject; var pair = await Pair(target);
        var item = (await AccessDocument(administrator, $"/users/{user}/sessions")).GetProperty("items")[0];
        Assert.True(item.GetProperty("canRevoke").GetBoolean());
        host.Services.GetRequiredService<IOptions<FoundationIdentityOptions>>().Value.OwnershipMode = OwnershipMode.ExternalOwned;
        Assert.False((await AccessDocument(administrator, $"/users/{user}/sessions")).GetProperty("items")[0].GetProperty("canRevoke").GetBoolean());
        using (var denied = await ManagementPost(host, administrator, $"/users/{user}/sessions/{item.GetProperty("id").GetString()}/revoke", new { expectedRevision = item.GetProperty("revision").GetString() }))
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var live = await Bearer(target, pair.AccessToken); Assert.Equal(HttpStatusCode.OK, live.StatusCode);
    }

    [Fact]
    public async Task A_failed_session_revoke_is_reported_as_failure_and_retry_works_while_notification_faults_do_not_undo_success()
    {
        var fault = new SessionFault(); var events = new CapturedSecurityEvents();
        await using var host = await HttpHost.Start(services =>
        {
            services.AddSingleton<IAuthEventSink>(events);
            var original = services.Single(descriptor => descriptor.ServiceType == typeof(IAuthenticationSessionStore));
            services.Replace(ServiceDescriptor.Scoped<IAuthenticationSessionStore>(provider => new FaultedSessions(
                (IAuthenticationSessionStore)ActivatorUtilities.CreateInstance(provider, original.ImplementationType!), fault)));
            services.EnsureReplacementContract<IAuthenticationSessionStore, FaultedSessions>();
        });
        using var administrator = host.Browser(); using var target = host.Browser();
        await host.LoginAdministrator(administrator); var callback = await host.StartAndAuthorize(target);
        using (var completed = await target.GetAsync(callback)) Assert.Equal(HttpStatusCode.Found, completed.StatusCode);
        var user = (await Session(target)).Subject!; var pair = await Pair(target);
        var page = await AccessDocument(administrator, $"/users/{user}/sessions"); var item = page.GetProperty("items")[0];
        fault.Revoke = true;
        using (var failed = await ManagementPost(host, administrator, $"/users/{user}/sessions/{item.GetProperty("id").GetString()}/revoke", new { expectedRevision = item.GetProperty("revision").GetString() }))
        {
            Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
            var text = await failed.Content.ReadAsStringAsync(); Assert.DoesNotContain("storage-secret-canary", text, StringComparison.Ordinal);
            Assert.False(JsonDocument.Parse(text).RootElement.GetProperty("revoked").GetBoolean());
        }
        using (var live = await Bearer(target, pair.AccessToken)) Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Contains(events.Items, notice => notice.Outcome == AuthEventOutcome.Failure && notice.Detail["result"] == "storage-unavailable");
        fault.Revoke = false; events.Throw = true;
        using (var retried = await ManagementPost(host, administrator, $"/users/{user}/sessions/{item.GetProperty("id").GetString()}/revoke", new { expectedRevision = item.GetProperty("revision").GetString() }))
            Assert.Equal(HttpStatusCode.OK, retried.StatusCode);
        using var dead = await Bearer(target, pair.AccessToken); Assert.Equal(HttpStatusCode.Unauthorized, dead.StatusCode);
        Assert.DoesNotContain("storage-secret-canary", JsonSerializer.Serialize(events.Items), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Administration_endpoints_declare_their_owner_API_denial_and_complete_dedicated_permissions()
    {
        await using var host = await HttpHost.Start();
        var endpoints = host.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName.StartsWith("ExternalIdentity", StringComparison.Ordinal) == true).ToArray();
        Assert.Equal(6, endpoints.Length);
        foreach (var endpoint in endpoints)
        {
            Assert.NotNull(endpoint.Metadata.GetMetadata<IDisableCookieRedirectMetadata>());
            Assert.Equal(typeof(Elsa.Foundation.Identity.ExternalAuthentication.Api.ExternalAuthenticationApiFeature).Assembly.GetName().Name,
                endpoint.Metadata.GetMetadata<EndpointOwnershipMetadata>()!.OwnerId);
            var name = endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()!.EndpointName;
            var permission = name switch
            { "ExternalIdentityLinks" => DefaultIdentityPermissionKeys.IdentityLinksRead, "ExternalIdentityAttachLink" or "ExternalIdentityRemoveLink" => DefaultIdentityPermissionKeys.IdentityLinksManage,
                "ExternalIdentitySessions" => DefaultIdentityPermissionKeys.IdentitySessionsRead, _ => DefaultIdentityPermissionKeys.IdentitySessionsManage };
            var parsed = new PermissionPolicyCodec().Parse(endpoint.Metadata.GetMetadata<EndpointSecurityDispositionMetadata>()!.Value!);
            Assert.Equal(name == "ExternalIdentityUsers" ? PermissionRequirementMode.Single : PermissionRequirementMode.All, parsed.Descriptor!.Mode);
            Assert.Equal((name == "ExternalIdentityUsers" ? new[] { DefaultIdentityPermissionKeys.IdentityUsersRead } : new[] { permission, DefaultIdentityPermissionKeys.IdentityUsersRead }).Select(PermissionKey.Normalize).Order(StringComparer.Ordinal), parsed.Descriptor.Permissions.Order(StringComparer.Ordinal));
        }
    }

    [Theory]
    [InlineData("tenant")]
    [InlineData("user")]
    [InlineData("bound")]
    public async Task Session_inspection_refuses_a_replacement_store_that_returns_another_scope_or_exceeds_the_page_bound(string boundary)
    {
        var fault = new SessionFault { PageBoundary = boundary };
        await using var host = await HttpHost.Start(services =>
        {
            var original = services.Single(descriptor => descriptor.ServiceType == typeof(IAuthenticationSessionStore));
            services.Replace(ServiceDescriptor.Scoped<IAuthenticationSessionStore>(provider => new FaultedSessions(
                (IAuthenticationSessionStore)ActivatorUtilities.CreateInstance(provider, original.ImplementationType!), fault)));
            services.EnsureReplacementContract<IAuthenticationSessionStore, FaultedSessions>();
        });
        using var administrator = host.Browser(); await host.LoginAdministrator(administrator);
        var user = (await Session(administrator)).Subject;
        using var denied = await administrator.GetAsync(Management + $"/users/{user}/sessions?take=1");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, denied.StatusCode);
        var body = await denied.Content.ReadAsStringAsync();
        Assert.DoesNotContain("foreign-private-canary", body, StringComparison.Ordinal);
        Assert.DoesNotContain("securityVersion", body, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("authenticated", (await Session(administrator)).Status);
    }

    private static async Task<JsonElement> AccessDocument(HttpClient client, string suffix)
    {
        using var response = await client.GetAsync(Management + suffix); var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {body}");
        using var document = JsonDocument.Parse(body); return document.RootElement.Clone();
    }
    private static async Task GrantAdministrator(HttpHost host, params string[] permissions)
    {
        await using var scope = host.Scope();
        var admin = (await scope.ServiceProvider.GetRequiredService<IUserStore>().FindByEmailAsync("default", TestAdmin.Email))!;
        var roles = scope.ServiceProvider.GetRequiredService<IRoleStore>();
        foreach (var role in admin.RoleIds)
            await roles.SaveAsync((await roles.FindAsync("default", role))! with { Permissions = permissions.ToHashSet(StringComparer.Ordinal) });
    }
    private sealed class CapturedSecurityEvents : IAuthEventSink
    {
        public ConcurrentQueue<AuthEvent> Items { get; } = new();
        public bool Throw;
        public ValueTask EmitAsync(AuthEvent authEvent, CancellationToken cancellationToken = default)
        { if (Throw) throw new InvalidOperationException("notification-secret-canary"); Items.Enqueue(authEvent); return ValueTask.CompletedTask; }
    }
    private sealed class SessionFault { public bool Revoke; public string? PageBoundary; }
    private sealed class FaultedSessions(IAuthenticationSessionStore inner, SessionFault fault) : IAuthenticationSessionStore
    {
        public bool IsDurable => inner.IsDurable;
        public ValueTask<bool> TryCreateAsync(AuthenticationSessionRecord session, CancellationToken cancellationToken = default) => inner.TryCreateAsync(session, cancellationToken);
        public ValueTask<AuthenticationSessionRecord?> FindAsync(string tenantId, string id, CancellationToken cancellationToken = default) => inner.FindAsync(tenantId, id, cancellationToken);
        public async ValueTask<IamPage<AuthenticationSessionRecord>> ListAsync(string tenantId, string userId, IamPageRequest page, CancellationToken cancellationToken = default)
        {
            var result = await inner.ListAsync(tenantId, userId, page, cancellationToken);
            if (fault.PageBoundary is null) return result;
            var item = result.Items[0] with
            {
                TenantId = fault.PageBoundary == "tenant" ? "foreign-private-canary" : tenantId,
                UserId = fault.PageBoundary == "user" ? "foreign-private-canary" : userId,
                Issuer = "foreign-private-canary"
            };
            return new(fault.PageBoundary == "bound" ? new[] { item, item } : new[] { item }, result.TotalCount);
        }
        public ValueTask<AuthenticationSessionRevocationStatus> RevokeAsync(string tenantId, string id, string expectedRevision, DateTimeOffset now, CancellationToken cancellationToken = default) =>
            fault.Revoke ? throw new InvalidOperationException("storage-secret-canary") : inner.RevokeAsync(tenantId, id, expectedRevision, now, cancellationToken);
    }
}
