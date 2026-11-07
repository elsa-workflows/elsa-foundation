using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.Core.Ownership;
using Elsa.Foundation.Identity.Core.Security;
using Elsa.Foundation.Identity.ExternalAuthentication.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Elsa.Foundation.Identity.Tests.ManagedOidc;

public sealed partial class ExternalAuthenticationHttpTests
{
    [Fact]
    public async Task Deployment_only_link_attachment_does_not_require_provider_configuration_persistence()
    {
        await using var host = await HttpHost.Start(databaseManagement: false);
        using var administrator = host.Browser(); using var external = host.Browser();
        await host.LoginAdministrator(administrator); await AddLinkTarget(host, "target");
        await using (var scope = host.Scope()) Assert.Null(scope.ServiceProvider.GetService<IProviderConfigurationStore>());
        var state = await AccessDocument(administrator, "/users/target/links");
        Assert.True(state.GetProperty("canAttach").GetBoolean());
        using (var linked = await ManagementPost(host, administrator, "/users/target/links/attach", AttachRequest(state)))
            Assert.Equal(HttpStatusCode.OK, linked.StatusCode);
        var callback = await host.StartAndAuthorize(external);
        using (var returned = await external.GetAsync(callback)) Assert.Equal(HttpStatusCode.Found, returned.StatusCode);
        Assert.Equal("target", (await Session(external)).Subject);
        await using var current = host.Scope(); Assert.Null(current.ServiceProvider.GetService<IProviderConfigurationStore>());
    }

    [Fact]
    public async Task Manual_linking_uses_an_existing_user_and_server_validated_issuer_refuses_rebinding_and_preserves_saved_roles()
    {
        var events = new CapturedSecurityEvents();
        await using var host = await HttpHost.Start(services => services.AddSingleton<IAuthEventSink>(events));
        using var administrator = host.Browser(); using var browser = host.Browser();
        await host.LoginAdministrator(administrator);
        await AddLinkTarget(host, "first-user"); await AddLinkTarget(host, "second-user");
        var before = await host.Counts(); var actor = (await Session(administrator)).Subject;
        var first = await AccessDocument(administrator, "/users/first-user/links");
        Assert.True(first.GetProperty("canAttach").GetBoolean());
        Assert.Contains(first.GetProperty("attachChoices").EnumerateArray(), item => item.GetProperty("id").GetString() == "deployment:company");
        using (var linked = await ManagementPost(host, administrator, "/users/first-user/links/attach", AttachRequest(first)))
        { Assert.Equal(HttpStatusCode.OK, linked.StatusCode); Assert.True((await linked.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("attached").GetBoolean()); }
        var second = await AccessDocument(administrator, "/users/second-user/links");
        using (var conflicting = await ManagementPost(host, administrator, "/users/second-user/links/attach", AttachRequest(second)))
            Assert.Equal(HttpStatusCode.Conflict, conflicting.StatusCode);
        Assert.Empty((await AccessDocument(administrator, "/users/second-user/links")).GetProperty("items").EnumerateArray());
        var links = await AccessDocument(administrator, "/users/first-user/links"); var saved = Assert.Single(links.GetProperty("items").EnumerateArray());
        Assert.Equal(host.Company.Issuer, saved.GetProperty("issuer").GetString()); Assert.Equal("opaque-Subject", saved.GetProperty("subject").GetString());
        Assert.Equal(0, host.Company.TokenRequests); // Linking tests metadata, never signs in or exchanges a code.
        Assert.Equal(before.Users, (await host.Counts()).Users); Assert.Equal(before.Tokens, (await host.Counts()).Tokens);
        Assert.Equal(actor, (await Session(administrator)).Subject);
        var callback = await host.StartAndAuthorize(browser);
        using (var completed = await browser.GetAsync(callback)) Assert.Equal(HttpStatusCode.Found, completed.StatusCode);
        Assert.Equal("first-user", (await Session(browser)).Subject); Assert.Equal(["reader"], (await Session(browser)).Roles);
        Assert.Equal(before.Users, (await host.Counts()).Users);
        Assert.Contains(events.Items, item => item.Category == AuthEventCategory.IdentityChange && item.Outcome == AuthEventOutcome.Success && item.Target == "first-user");
        Assert.Contains(events.Items, item => item.Outcome == AuthEventOutcome.Denied && item.Target == "second-user");
        Assert.DoesNotContain("opaque-Subject", JsonSerializer.Serialize(events.Items), StringComparison.Ordinal);
        Assert.DoesNotContain("CredentialBinding", JsonSerializer.Serialize(events.Items), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("role")]
    [InlineData("direct")]
    [InlineData("membership")]
    [InlineData("ownership")]
    public async Task Link_administration_cannot_attach_an_identity_to_an_account_beyond_current_delegation_or_ownership(string boundary)
    {
        await using var host = await HttpHost.Start(); using var administrator = host.Browser();
        await host.LoginAdministrator(administrator); await AddLinkTarget(host, "target");
        if (boundary != "ownership")
        {
            await using var scope = host.Scope(); var users = scope.ServiceProvider.GetRequiredService<IUserStore>();
            var target = (await users.FindAsync("default", "target"))!;
            if (boundary == "role")
            {
                await scope.ServiceProvider.GetRequiredService<IRoleStore>().SaveAsync(new("privileged", "default", "Privileged", null, new HashSet<string> { "*" }, false));
                await users.SaveAsync(target with { RoleIds = new HashSet<string> { "privileged" } });
            }
            if (boundary == "direct") await users.SaveAsync(target with { RoleIds = new HashSet<string>(), DirectPermissions = new HashSet<string> { "*" } });
            if (boundary == "membership") await scope.ServiceProvider.GetRequiredService<ITenantMembershipStore>().SaveAsync(new("default", "target",
                TenantMembershipStatus.Active, new HashSet<string>(), new HashSet<string> { "*" }));
            await GrantAdministrator(host, DefaultIdentityPermissionKeys.IdentityUsersRead, DefaultIdentityPermissionKeys.IdentityLinksRead, DefaultIdentityPermissionKeys.IdentityLinksManage);
        }
        else host.Services.GetRequiredService<IOptions<FoundationIdentityOptions>>().Value.OwnershipMode = OwnershipMode.ExternalOwned;
        var state = await AccessDocument(administrator, "/users/target/links"); Assert.False(state.GetProperty("canAttach").GetBoolean());
        using var denied = await ManagementPost(host, administrator, "/users/target/links/attach", AttachRequest(state));
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Empty((await AccessDocument(administrator, "/users/target/links")).GetProperty("items").EnumerateArray());
        Assert.Equal(0, host.Company.TokenRequests);
    }

    [Theory]
    [InlineData("user-revision", HttpStatusCode.Conflict)]
    [InlineData("catalog-revision", HttpStatusCode.Conflict)]
    [InlineData("issuer-injection", HttpStatusCode.BadRequest)]
    [InlineData("missing-source", HttpStatusCode.NotFound)]
    [InlineData("source-change", HttpStatusCode.Conflict)]
    public async Task Link_attachment_refuses_stale_source_user_or_unknown_issuer_fields_before_writing(string invalid, HttpStatusCode expected)
    {
        await using var host = await HttpHost.Start(); using var administrator = host.Browser();
        await host.LoginAdministrator(administrator); await AddLinkTarget(host, "target");
        var state = await AccessDocument(administrator, "/users/target/links");
        var request = new Dictionary<string, object?>
        {
            ["connectionRecordId"] = invalid == "missing-source" ? "missing" : "deployment:company", ["subject"] = "opaque-Subject",
            ["expectedUserRevision"] = invalid == "user-revision" ? "stale" : state.GetProperty("userRevision").GetString(),
            ["expectedCatalogRevision"] = invalid == "catalog-revision" ? "stale" : state.GetProperty("catalogRevision").GetString()
        };
        if (invalid == "issuer-injection") request["issuer"] = "https://untrusted.example";
        if (invalid == "source-change") host.Company.ChangeMetadata = _ => host.Options.Connections[0] = host.Options.Connections[0] with { Admission = new() { Type = "reject" } };
        using var denied = await ManagementPost(host, administrator, "/users/target/links/attach", request);
        Assert.Equal(expected, denied.StatusCode);
        Assert.Empty((await AccessDocument(administrator, "/users/target/links")).GetProperty("items").EnumerateArray());
    }

    [Theory]
    [InlineData("take=101")]
    [InlineData("skip=-1")]
    [InlineData("take=1&take=2")]
    public async Task Link_and_session_inspection_refuse_unbounded_or_ambiguous_paging(string query)
    {
        await using var host = await HttpHost.Start(); using var administrator = host.Browser();
        await host.LoginAdministrator(administrator); var user = (await Session(administrator)).Subject;
        foreach (var resource in new[] { "links", "sessions" })
        {
            using var denied = await administrator.GetAsync(Management + $"/users/{user}/{resource}?{query}");
            Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        }
    }

    private static object AttachRequest(JsonElement state) => new
    {
        connectionRecordId = "deployment:company", subject = "opaque-Subject",
        expectedCatalogRevision = state.GetProperty("catalogRevision").GetString(), expectedUserRevision = state.GetProperty("userRevision").GetString()
    };
    private static async Task AddLinkTarget(HttpHost host, string id)
    {
        await using var scope = host.Scope();
        await scope.ServiceProvider.GetRequiredService<IUserStore>().SaveAsync(new(id, "default", id, null, id,
            UserStatus.Active, ResourceOwnership.Foundation, new HashSet<string> { "reader" }, new HashSet<string>()));
    }
}
