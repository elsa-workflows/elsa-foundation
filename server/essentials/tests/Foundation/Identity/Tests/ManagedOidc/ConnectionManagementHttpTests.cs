using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Core.Iam;
using Elsa.Foundation.Identity.ExternalAuthentication.Core;
using Elsa.Api.AspNetCore;
using Elsa.Foundation.Identity.ExternalAuthentication.Api;
using Elsa.Foundation.Identity.Authorization;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Elsa.Api.Capabilities.Models;
using Elsa.Foundation.Identity.ExternalAuthentication;
using Microsoft.Extensions.Options;

namespace Elsa.Foundation.Identity.Tests.ManagedOidc;

public sealed partial class ExternalAuthenticationHttpTests
{
    private const string Management = "_elsa/identity/external-connections";

    [Fact]
    public async Task Management_discovery_and_form_metadata_describe_the_installed_client_contract_and_current_read_only_grants()
    {
        await using var host = await HttpHost.Start();
        var declaration = Assert.Single(host.Services.GetServices<ApiCapabilityDeclaration>(), value => value.CapabilityId == "elsa.api.identity.external-authentication");
        Assert.Contains(declaration.Links, link => link.Href == "_elsa/identity/external-connections");
        using var browser = host.Browser();
        using (var csrf = await browser.GetAsync("identity/external/antiforgery"))
        {
            Assert.Equal(HttpStatusCode.OK, csrf.StatusCode);
            using var json = JsonDocument.Parse(await csrf.Content.ReadAsStreamAsync());
            Assert.Equal("__csrf", json.RootElement.GetProperty("formFieldName").GetString());
            Assert.Equal("RequestVerificationToken", json.RootElement.GetProperty("headerName").GetString());
        }
        await using (var scope = host.Scope())
        {
            var roles = scope.ServiceProvider.GetRequiredService<IRoleStore>();
            await roles.SaveAsync((await roles.FindAsync("default", "reader"))! with { Permissions = new HashSet<string> { DefaultIdentityPermissionKeys.IdentityProvidersRead } });
        }
        var callback = await host.StartAndAuthorize(browser);
        using (var returned = await browser.GetAsync(callback)) Assert.Equal(HttpStatusCode.Found, returned.StatusCode);
        using var catalog = await browser.GetAsync(Management);
        Assert.Equal(HttpStatusCode.OK, catalog.StatusCode);
        using var response = JsonDocument.Parse(await catalog.Content.ReadAsStreamAsync());
        Assert.False(response.RootElement.GetProperty("canManageConnections").GetBoolean());
        Assert.Contains(response.RootElement.GetProperty("provisioningPolicies").EnumerateArray(), policy => policy.GetString() == "reject");
        var company = Assert.Single(response.RootElement.GetProperty("connections").EnumerateArray(), row => row.GetProperty("document").GetProperty("key").GetString() == "company");
        Assert.True(company.GetProperty("configurationSupported").GetBoolean());
        Assert.False(company.GetProperty("configurationEditable").GetBoolean());
    }

    [Fact]
    public async Task Preview_form_accepts_antiforgery_created_through_the_administrators_bearer_transport_without_replacing_the_cookie()
    {
        await using var host = await HttpHost.Start();
        using var browser = host.Browser();
        await host.LoginAdministrator(browser);
        var pair = await Pair(browser);
        var before = await host.Counts();
        var administrator = await Session(browser);
        using var tokenRequest = new HttpRequestMessage(HttpMethod.Get, "identity/external/antiforgery?purpose=preview");
        tokenRequest.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", pair.AccessToken);
        using var tokenResponse = await browser.SendAsync(tokenRequest);
        Assert.Equal(HttpStatusCode.OK, tokenResponse.StatusCode);
        using var json = JsonDocument.Parse(await tokenResponse.Content.ReadAsStreamAsync());
        var field = json.RootElement.GetProperty("formFieldName").GetString()!;
        var token = json.RootElement.GetProperty("requestToken").GetString()!;
        using var started = await browser.PostAsync("identity/external/preview/company", new FormUrlEncodedContent(new Dictionary<string, string>
            { [field] = token, ["connectionRecordId"] = "deployment:company", ["returnUrl"] = "/backend/resource" }));
        Assert.Equal(HttpStatusCode.Found, started.StatusCode);
        using var upstream = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false });
        using var authorized = await upstream.GetAsync(started.Headers.Location);
        using var returned = await browser.GetAsync(authorized.Headers.Location);
        Assert.Equal(HttpStatusCode.Found, returned.StatusCode);
        Assert.Equal(administrator.Subject, (await Session(browser)).Subject);
        Assert.Equal(before, await host.Counts());
    }

    [Fact]
    public async Task Preview_preparation_refuses_a_different_administrators_cookie_and_bearer_even_when_both_have_management_permission()
    {
        await using var host = await HttpHost.Start();
        using var administrator = host.Browser(); using var external = host.Browser();
        await host.LoginAdministrator(administrator); await MakeAdministrative(host, "company");
        var callback = await host.StartAndAuthorize(external);
        using (var returned = await external.GetAsync(callback)) Assert.Equal(HttpStatusCode.Found, returned.StatusCode);
        var pair = await Pair(external);
        var first = await Session(administrator); var second = await Session(external);
        Assert.NotEqual(first.Subject, second.Subject);
        var before = await host.Counts();
        using var request = new HttpRequestMessage(HttpMethod.Get, "identity/external/antiforgery?purpose=preview");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", pair.AccessToken);
        using var denied = await administrator.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        Assert.Equal(before, await host.Counts());
        Assert.Equal(first.Subject, (await Session(administrator)).Subject);
        Assert.Equal(second.Subject, (await Session(external)).Subject);
    }

    [Fact]
    public async Task Management_endpoints_declare_api_denial_behavior_and_their_complete_permission_requirements()
    {
        await using var host = await HttpHost.Start();
        var endpoints = host.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()?.EndpointName.StartsWith("ExternalConnections", StringComparison.Ordinal) == true).ToArray();
        Assert.Equal(7, endpoints.Length);
        foreach (var endpoint in endpoints)
        {
            Assert.Equal(typeof(ExternalAuthenticationApiFeature).Assembly.GetName().Name, endpoint.Metadata.GetMetadata<EndpointOwnershipMetadata>()!.OwnerId);
            Assert.NotNull(endpoint.Metadata.GetMetadata<IDisableCookieRedirectMetadata>());
            var metadata = endpoint.Metadata.GetMetadata<EndpointSecurityDispositionMetadata>()!;
            Assert.Equal(EndpointSecurityDispositionKind.Permission, metadata.Kind);
            var parsed = new PermissionPolicyCodec().Parse(metadata.Value!);
            Assert.Equal(PermissionPolicyParseStatus.Valid, parsed.Status);
            var name = endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()!.EndpointName;
            var expected = name switch
            {
                "ExternalConnectionsUsers" => new[] { DefaultIdentityPermissionKeys.IdentityUsersRead, DefaultIdentityPermissionKeys.IdentityProvidersManage },
                "ExternalConnectionsRoles" => [DefaultIdentityPermissionKeys.IdentityRolesRead, DefaultIdentityPermissionKeys.IdentityProvidersManage],
                "ExternalConnectionsList" or "ExternalConnectionsDetail" => [DefaultIdentityPermissionKeys.IdentityProvidersRead],
                _ => [DefaultIdentityPermissionKeys.IdentityProvidersManage]
            };
            Assert.Equal(expected.Select(PermissionKey.Normalize).Order(StringComparer.Ordinal), parsed.Descriptor!.Permissions.Order(StringComparer.Ordinal));
        }
    }

    [Fact]
    public async Task Management_metadata_requires_permission_and_projects_only_known_settings_and_secret_references()
    {
        await using var host = await HttpHost.Start();
        using var anonymous = host.Browser();
        using (var denied = await anonymous.GetAsync(Management)) Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
        using var reader = host.Browser();
        var callback = await host.StartAndAuthorize(reader);
        using (var returned = await reader.GetAsync(callback)) Assert.Equal(HttpStatusCode.Found, returned.StatusCode);
        using (var denied = await reader.GetAsync(Management)) Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var administrator = host.Browser();
        await host.LoginAdministrator(administrator);
        host.Options.Connections[0] = host.Options.Connections[0] with { Settings = new Dictionary<string, string>(host.Options.Connections[0].Settings)
            { ["futurePrivateSetting"] = "settings-canary-value" } };
        using var response = await administrator.GetAsync(Management);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.Headers.CacheControl!.NoStore);
        var text = await response.Content.ReadAsStringAsync();
        foreach (var secret in new[] { "settings-canary-value", "futurePrivateSetting", "credentialBinding", "protectedProofs", ControlledOidcServer.ClientSecret })
            Assert.DoesNotContain(secret, text, StringComparison.OrdinalIgnoreCase);
        using var json = JsonDocument.Parse(text);
        var company = Assert.Single(json.RootElement.GetProperty("connections").EnumerateArray(),
            row => row.GetProperty("document").GetProperty("key").GetString() == "company");
        Assert.False(company.GetProperty("configurationEditable").GetBoolean());
        Assert.Equal("client-company", company.GetProperty("document").GetProperty("secrets").GetProperty("clientSecret").GetProperty("name").GetString());
        Assert.Equal(new Uri(host.BaseUri, "identity/external/signin/company/callback").AbsoluteUri, company.GetProperty("signInCallback").GetString());
        Assert.False(json.RootElement.GetProperty("capabilities").GetProperty("canMapGroups").GetBoolean());
    }

    [Fact]
    public async Task Management_assess_save_test_enable_and_retire_use_revisions_without_authenticating_the_test_identity()
    {
        await using var host = await HttpHost.Start();
        using var administrator = host.Browser();
        await host.LoginAdministrator(administrator);
        var before = await host.Counts();
        var draft = host.Options.Connections[0] with { Id = "", Key = "draft", Enabled = true, Preferred = false };
        var create = new ConnectionWrite(ConnectionChange.Create, null, draft);
        using (var assessed = await ManagementPost(host, administrator, "/assess", create))
        {
            Assert.Equal(HttpStatusCode.OK, assessed.StatusCode);
            using var json = JsonDocument.Parse(await assessed.Content.ReadAsStreamAsync());
            Assert.True(json.RootElement.GetProperty("canApply").GetBoolean());
            var proposed = Assert.Single(json.RootElement.GetProperty("proposed").GetProperty("connections").EnumerateArray(),
                row => row.GetProperty("document").GetProperty("key").GetString() == "draft");
            Assert.False(proposed.GetProperty("document").GetProperty("enabled").GetBoolean());
        }
        Assert.Null((await Catalog(host)).Revision);
        var created = await ManagementChange(host, administrator, create);
        var saved = Assert.Single((await Catalog(host)).Records);
        Assert.False(saved.Enabled);
        Assert.Equal(created.RecordId, saved.Id);
        using (var stale = await ManagementPost(host, administrator, "/commands", create)) Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using (var detail = await administrator.GetAsync(Management + "/" + saved.Id)) Assert.Equal(HttpStatusCode.OK, detail.StatusCode);
        using (var tested = await ManagementPost(host, administrator, "/test", new { connectionRecordId = saved.Id, expectedRevision = created.Revision }))
        {
            Assert.Equal(HttpStatusCode.OK, tested.StatusCode);
            var text = await tested.Content.ReadAsStringAsync();
            Assert.DoesNotContain("credentialBinding", text, StringComparison.OrdinalIgnoreCase);
            using var json = JsonDocument.Parse(text);
            Assert.True(json.RootElement.GetProperty("readiness").GetProperty("ready").GetBoolean());
            Assert.Equal(0, json.RootElement.GetProperty("protocolFailure").GetInt32());
            Assert.False(json.RootElement.GetProperty("stale").GetBoolean());
        }
        Assert.Equal(0, host.Company.TokenRequests);
        Assert.Equal(before, await host.Counts());
        var enabled = await ManagementChange(host, administrator, new(ConnectionChange.Enable, created.Revision, saved));
        Assert.True(Assert.Single((await Catalog(host)).Records).Enabled);
        var disabled = await ManagementChange(host, administrator, new(ConnectionChange.Disable, enabled.Revision, saved));
        var archived = await ManagementChange(host, administrator, new(ConnectionChange.Archive, disabled.Revision, saved));
        Assert.NotNull(Assert.Single((await Catalog(host)).Records).ArchivedAt);
        var restored = await ManagementChange(host, administrator, new(ConnectionChange.Restore, archived.Revision, saved));
        Assert.Null(Assert.Single((await Catalog(host)).Records).ArchivedAt);
        await ManagementChange(host, administrator, new(ConnectionChange.Remove, restored.Revision, saved));
        Assert.Empty((await Catalog(host)).Records);
    }

    [Fact]
    public async Task Management_collision_requires_explicit_override_and_reports_the_effect_before_saving()
    {
        await using var host = await HttpHost.Start();
        using var administrator = host.Browser();
        await host.LoginAdministrator(administrator);
        var record = host.Options.Connections[0] with { Id = "", Enabled = false };
        var created = await ManagementChange(host, administrator, new(ConnectionChange.Create, null, record));
        var stored = Assert.Single((await Catalog(host)).Records);
        Assert.Equal("deployment:company", (await View(host, "company")).Record.Id);
        host.Options.AllowDeploymentOverrides = true;
        using (var assessed = await ManagementPost(host, administrator, "/assess", new ConnectionWrite(ConnectionChange.PromoteOverride, created.Revision, stored)))
        {
            Assert.Equal(HttpStatusCode.OK, assessed.StatusCode);
            using var json = JsonDocument.Parse(await assessed.Content.ReadAsStreamAsync());
            var effective = Assert.Single(json.RootElement.GetProperty("proposed").GetProperty("connections").EnumerateArray(),
                row => row.GetProperty("document").GetProperty("key").GetString() == "company" && row.GetProperty("effective").GetBoolean());
            Assert.Equal(stored.Id, effective.GetProperty("document").GetProperty("id").GetString());
            Assert.False(effective.GetProperty("document").GetProperty("enabled").GetBoolean());
        }
        Assert.Equal("deployment:company", (await View(host, "company")).Record.Id);
        var promoted = await ManagementChange(host, administrator, new(ConnectionChange.PromoteOverride, created.Revision, stored));
        Assert.Equal(stored.Id, (await View(host, "company")).Record.Id);
        Assert.False((await View(host, "company")).Record.Enabled);
        var archived = await ManagementChange(host, administrator, new(ConnectionChange.Archive, promoted.Revision, stored));
        Assert.Equal("deployment:company", (await View(host, "company")).Record.Id);
        await ManagementChange(host, administrator, new(ConnectionChange.Restore, archived.Revision, stored));
        Assert.Equal(stored.Id, (await View(host, "company")).Record.Id);
    }

    [Fact]
    public async Task Management_test_flags_a_material_change_observed_during_actual_discovery()
    {
        await using var host = await HttpHost.Start();
        using var administrator = host.Browser();
        await host.LoginAdministrator(administrator);
        var before = await host.Counts();
        host.Company.ChangeMetadata = _ => host.Options.Connections[0] = host.Options.Connections[0] with
            { Settings = new Dictionary<string, string>(host.Options.Connections[0].Settings) { ["clientId"] = "changed-during-discovery" } };
        using var response = await ManagementPost(host, administrator, "/test", new { connectionRecordId = "deployment:company", expectedRevision = (string?)null });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        Assert.True(json.RootElement.GetProperty("stale").GetBoolean());
        Assert.Equal(0, host.Company.TokenRequests);
        Assert.Equal(before, await host.Counts());
    }

    [Theory]
    [InlineData("csrf", HttpStatusCode.BadRequest)]
    [InlineData("size", HttpStatusCode.RequestEntityTooLarge)]
    [InlineData("media", HttpStatusCode.UnsupportedMediaType)]
    [InlineData("mapping", HttpStatusCode.BadRequest)]
    [InlineData("inline-secret", HttpStatusCode.BadRequest)]
    [InlineData("issuer-credentials", HttpStatusCode.BadRequest)]
    [InlineData("enum", HttpStatusCode.BadRequest)]
    [InlineData("protocol", HttpStatusCode.BadRequest)]
    [InlineData("deployment", HttpStatusCode.NotFound)]
    [InlineData("database", HttpStatusCode.Forbidden)]
    [InlineData("role", HttpStatusCode.Forbidden)]
    public async Task Management_write_boundaries_refuse_without_saving_a_catalog(string invalid, HttpStatusCode expected)
    {
        await using var host = await HttpHost.Start();
        using var administrator = host.Browser();
        await host.LoginAdministrator(administrator);
        var record = host.Options.Connections[0] with { Id = "", Key = "new" };
        if (invalid == "inline-secret") record = record with { Settings = new Dictionary<string, string>(record.Settings) { ["clientSecret"] = "canary" } };
        if (invalid == "issuer-credentials") record = record with { Settings = new Dictionary<string, string>(record.Settings) { ["authority"] = "https://user:password@identity.example/issuer" } };
        if (invalid == "protocol") record = record with { Protocol = "uninstalled" };
        if (invalid == "role") host.Options.AllowedRoleIds = ["other-role"];
        if (invalid == "database") host.Options.AllowDatabaseManagement = false;
        var command = new ConnectionWrite(invalid == "deployment" ? ConnectionChange.Disable : invalid == "enum" ? (ConnectionChange)200 : ConnectionChange.Create,
            null, record with { Id = invalid == "deployment" ? "deployment:company" : record.Id });
        using var request = new HttpRequestMessage(HttpMethod.Post, Management + "/commands");
        if (invalid != "csrf") request.Headers.Add("RequestVerificationToken", await host.Csrf(administrator));
        request.Content = invalid switch
        {
            "size" => new StringContent(new string('x', 65537), Encoding.UTF8, "application/json"),
            "media" => new StringContent("{}", Encoding.UTF8, "text/plain"),
            "mapping" => new StringContent("{\"change\":0,\"expectedRevision\":null,\"record\":{\"key\":\"new\",\"roleMappings\":[]}}", Encoding.UTF8, "application/json"),
            _ => JsonContent.Create(command)
        };
        using var response = await administrator.SendAsync(request);
        Assert.Equal(expected, response.StatusCode);
        Assert.Null((await Catalog(host)).Revision);
        Assert.Equal(0, host.Company.TokenRequests);
    }

    [Fact]
    public async Task Management_enable_delegates_the_saved_policy_against_current_grants_even_when_the_body_omits_its_roles()
    {
        await using var host = await HttpHost.Start();
        using var administrator = host.Browser();
        await host.LoginAdministrator(administrator); // The cookie initially contains the seeded wildcard grant.
        var created = await ManagementChange(host, administrator, new(ConnectionChange.Create, null,
            host.Options.Connections[0] with { Id = "", Key = "new" }));
        var stored = Assert.Single((await Catalog(host)).Records);
        await using var scope = host.Scope();
        var admin = (await scope.ServiceProvider.GetRequiredService<IUserStore>().FindByEmailAsync("default", TestAdmin.Email))!;
        var roles = scope.ServiceProvider.GetRequiredService<IRoleStore>();
        foreach (var id in admin.RoleIds)
            await roles.SaveAsync((await roles.FindAsync("default", id))! with { Permissions = new HashSet<string>
                { DefaultIdentityPermissionKeys.IdentityProvidersManage, DefaultIdentityPermissionKeys.IdentityProvidersRead,
                    DefaultIdentityPermissionKeys.IdentityUsersRead, DefaultIdentityPermissionKeys.IdentityRolesRead } });
        await roles.SaveAsync((await roles.FindAsync("default", "reader"))! with { Permissions = new HashSet<string> { "*" } });
        using var denied = await ManagementPost(host, administrator, "/commands", new ConnectionWrite(ConnectionChange.Enable, created.Revision,
            stored with { Admission = new() }));
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.False(Assert.Single((await Catalog(host)).Records).Enabled);
        using var lookup = await administrator.GetAsync(Management + "/lookups/roles");
        Assert.Equal(HttpStatusCode.OK, lookup.StatusCode);
        using var json = JsonDocument.Parse(await lookup.Content.ReadAsStreamAsync());
        var reader = Assert.Single(json.RootElement.GetProperty("items").EnumerateArray(), item => item.GetProperty("id").GetString() == "reader");
        Assert.False(reader.GetProperty("canDelegate").GetBoolean());
    }

    [Fact]
    public async Task Management_rechecks_saved_permissions_after_a_cookie_was_issued_and_bounds_its_lookups()
    {
        await using var host = await HttpHost.Start();
        using var administrator = host.Browser();
        await host.LoginAdministrator(administrator);
        using (var roles = await administrator.GetAsync(Management + "/lookups/roles?take=1"))
        {
            Assert.Equal(HttpStatusCode.OK, roles.StatusCode);
            using var json = JsonDocument.Parse(await roles.Content.ReadAsStreamAsync());
            Assert.Single(json.RootElement.GetProperty("items").EnumerateArray());
        }
        using (var users = await administrator.GetAsync(Management + "/lookups/users?take=1"))
        {
            Assert.Equal(HttpStatusCode.OK, users.StatusCode);
            var text = await users.Content.ReadAsStringAsync();
            Assert.DoesNotContain("password", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("securityStamp", text, StringComparison.OrdinalIgnoreCase);
            using var json = JsonDocument.Parse(text);
            Assert.Single(json.RootElement.GetProperty("items").EnumerateArray());
        }
        foreach (var query in new[] { "take=101", "skip=-1", "take=1&take=2", "skip=2147483648" })
        {
            using var bad = await administrator.GetAsync(Management + "/lookups/users?" + query);
            Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        }
        await using var scope = host.Scope();
        var userStore = scope.ServiceProvider.GetRequiredService<IUserStore>();
        var admin = (await userStore.FindByEmailAsync("default", TestAdmin.Email))!;
        foreach (var id in admin.RoleIds)
        {
            var roles = scope.ServiceProvider.GetRequiredService<IRoleStore>();
            await roles.SaveAsync((await roles.FindAsync("default", id))! with { Permissions = new HashSet<string>() });
        }
        using (var stale = await administrator.GetAsync(Management)) Assert.Equal(HttpStatusCode.Forbidden, stale.StatusCode);
        using (var stale = await ManagementPost(host, administrator, "/commands", new ConnectionWrite(ConnectionChange.Create, null,
            host.Options.Connections[0] with { Id = "", Key = "new" }))) Assert.Equal(HttpStatusCode.Forbidden, stale.StatusCode);
        Assert.Null((await Catalog(host)).Revision);
    }

    private static async Task<HttpResponseMessage> ManagementPost(HttpHost host, HttpClient client, string suffix, object value)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Management + suffix) { Content = JsonContent.Create(value, value.GetType()) };
        request.Headers.Add("RequestVerificationToken", await host.Csrf(client));
        return await client.SendAsync(request);
    }
    private static async Task<ConnectionWriteResult> ManagementChange(HttpHost host, HttpClient client, ConnectionWrite command)
    {
        using var response = await ManagementPost(host, client, "/commands", command);
        var text = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, $"{response.StatusCode}: {text}");
        var result = JsonSerializer.Deserialize<ConnectionWriteResult>(text, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.True(result.Saved);
        return result;
    }
    private static async Task<ConnectionCatalog> Catalog(HttpHost host)
    {
        await using var scope = host.Scope();
        return await scope.ServiceProvider.GetRequiredService<IConnectionCatalogStore>().ReadAsync();
    }
}
