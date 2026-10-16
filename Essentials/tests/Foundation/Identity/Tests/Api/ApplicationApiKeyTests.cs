using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Api.AspNetCore;
using Elsa.Api.Compatibility.Testing.Manifests;
using Elsa.Api.Compatibility.Testing.Security;
using Elsa.Foundation.Identity.Authorization;
using Elsa.Foundation.Identity.Core.Authorization;
using Elsa.Foundation.Identity.Core.Iam;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Foundation.Identity.Tests.Api;

/// <summary>
/// Applications and their API keys (#2775), end to end over the default first-party composition: the seeded admin
/// manages applications through <c>/_elsa/identity/applications</c> with a session cookie, and each key authenticates
/// through the OpenIddict scheme selector as the application, with exactly its roles' permissions.
/// </summary>
[Collection(FastEndpointsHostCollection.Name)]
public sealed class ApplicationApiKeyTests : IAsyncLifetime
{
    private const string Tenant = "default";
    private const string ApplicationsRoute = "/_elsa/identity/applications";
    private const string SessionRoute = "/_elsa/identity/session";
    private const string ReaderRole = "application-readers";
    private const string OperatorRole = "operators";

    private TokenEndpointFixture _fixture = null!;
    private HttpClient _admin = null!;

    public async Task InitializeAsync()
    {
        _fixture = await TokenEndpointFixture.StartAsync();
        _admin = _fixture.Client;
        await LoginTestHelper.LoginAsync(_admin);
        await using var scope = _fixture.Services.CreateAsyncScope();
        var roles = scope.ServiceProvider.GetRequiredService<IRoleStore>();
        await roles.SaveAsync(new RoleRecord(ReaderRole, Tenant, "Application readers", null, Set(DefaultIdentityPermissionKeys.IdentityApplicationsRead), System: false));
        await roles.SaveAsync(new RoleRecord(OperatorRole, Tenant, "Operators", null, Set("workflows/runtime:read", DefaultIdentityPermissionKeys.IdentityUsersRead), System: false));
    }

    public async Task DisposeAsync() => await _fixture.DisposeAsync();

    [Fact]
    public async Task A_key_authenticates_as_its_application_with_exactly_its_roles_permissions()
    {
        var (applicationId, apiKey) = await CreateApplicationAsync(ReaderRole, OperatorRole);

        using var session = JsonDocument.Parse(await (await SendWithKeyAsync(HttpMethod.Get, SessionRoute, apiKey)).Content.ReadAsStringAsync());
        var root = session.RootElement;

        Assert.Equal("authenticated", root.GetProperty("status").GetString());
        Assert.Equal(applicationId, root.GetProperty("subject").GetString());
        Assert.Equal(Tenant, root.GetProperty("tenantId").GetString());
        Assert.Equal("api-key", root.GetProperty("provider").GetString());
        Assert.Equal([ReaderRole, OperatorRole], Strings(root.GetProperty("roles")));
        Assert.Equal(
            [DefaultIdentityPermissionKeys.IdentityApplicationsRead, DefaultIdentityPermissionKeys.IdentityUsersRead, "workflows/runtime:read"],
            Strings(root.GetProperty("permissions")));
    }

    [Fact]
    public async Task The_roles_permissions_are_enforced_on_the_key()
    {
        var (_, apiKey) = await CreateApplicationAsync(ReaderRole);

        Assert.Equal(HttpStatusCode.OK, (await SendWithKeyAsync(HttpMethod.Get, ApplicationsRoute, apiKey)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendWithKeyAsync(HttpMethod.Post, ApplicationsRoute, apiKey, new { displayName = "Escalated" })).StatusCode);
    }

    [Fact]
    public async Task Only_a_hash_of_the_key_is_stored_and_no_read_returns_the_key_or_its_hash()
    {
        var created = await _admin.PostAsJsonAsync(ApplicationsRoute, new { displayName = "Hashed", roleIds = new[] { ReaderRole } });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.True(created.Headers.CacheControl?.NoStore);
        var (applicationId, apiKey) = await ReadIssuedAsync(created);

        var credential = await FindCredentialAsync(applicationId);
        var hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(apiKey)));
        Assert.Equal(hash, credential.HashedSecret);
        Assert.Equal("sha256", credential.HashAlgorithm);
        Assert.DoesNotContain(apiKey.Split('.')[^1], credential.HashedSecret, StringComparison.Ordinal);

        foreach (var read in new[] { ApplicationsRoute, $"{ApplicationsRoute}/{applicationId}" })
        {
            var body = await _admin.GetStringAsync(read);
            Assert.Contains(applicationId, body, StringComparison.Ordinal);
            Assert.DoesNotContain(apiKey, body, StringComparison.Ordinal);
            Assert.DoesNotContain(apiKey.Split('.')[^1], body, StringComparison.Ordinal);
            Assert.DoesNotContain(hash, body, StringComparison.Ordinal);
        }

        var updated = await (await _admin.PutAsJsonAsync($"{ApplicationsRoute}/{applicationId}", new { displayName = "Renamed", roleIds = new[] { ReaderRole }, enabled = true })).Content.ReadAsStringAsync();
        Assert.DoesNotContain(apiKey, updated, StringComparison.Ordinal);
        Assert.DoesNotContain(hash, updated, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_rotated_out_key_gets_401_and_the_new_key_authenticates()
    {
        var (applicationId, oldKey) = await CreateApplicationAsync(ReaderRole);

        var rotated = await _admin.PostAsync($"{ApplicationsRoute}/{applicationId}/api-key/rotate", null);
        Assert.Equal(HttpStatusCode.OK, rotated.StatusCode);
        Assert.True(rotated.Headers.CacheControl?.NoStore);
        var newKey = await ReadApiKeyAsync(rotated);

        Assert.NotEqual(oldKey, newKey);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendWithKeyAsync(HttpMethod.Get, ApplicationsRoute, oldKey)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await SendWithKeyAsync(HttpMethod.Get, ApplicationsRoute, newKey)).StatusCode);
    }

    [Fact]
    public async Task A_revoked_key_gets_401_at_once_and_a_rotation_replaces_it()
    {
        var (applicationId, apiKey) = await CreateApplicationAsync(ReaderRole);
        Assert.Equal(HttpStatusCode.OK, (await SendWithKeyAsync(HttpMethod.Get, ApplicationsRoute, apiKey)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await _admin.PostAsync($"{ApplicationsRoute}/{applicationId}/api-key/revoke", null)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await SendWithKeyAsync(HttpMethod.Get, ApplicationsRoute, apiKey)).StatusCode);
        Assert.Equal("revoked", await ApiKeyStatusAsync(applicationId));
        var replacement = await ReadApiKeyAsync(await _admin.PostAsync($"{ApplicationsRoute}/{applicationId}/api-key/rotate", null));
        Assert.Equal(HttpStatusCode.OK, (await SendWithKeyAsync(HttpMethod.Get, ApplicationsRoute, replacement)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendWithKeyAsync(HttpMethod.Get, ApplicationsRoute, apiKey)).StatusCode);
    }

    [Fact]
    public async Task A_disabled_application_gets_401_until_it_is_enabled_and_a_deleted_one_for_good()
    {
        var (applicationId, apiKey) = await CreateApplicationAsync(ReaderRole);

        await SetEnabledAsync(applicationId, enabled: false);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendWithKeyAsync(HttpMethod.Get, ApplicationsRoute, apiKey)).StatusCode);

        await SetEnabledAsync(applicationId, enabled: true);
        Assert.Equal(HttpStatusCode.OK, (await SendWithKeyAsync(HttpMethod.Get, ApplicationsRoute, apiKey)).StatusCode);

        Assert.Equal(HttpStatusCode.NoContent, (await _admin.DeleteAsync($"{ApplicationsRoute}/{applicationId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendWithKeyAsync(HttpMethod.Get, ApplicationsRoute, apiKey)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _admin.GetAsync($"{ApplicationsRoute}/{applicationId}")).StatusCode);
        await using var scope = _fixture.Services.CreateAsyncScope();
        Assert.Null(await scope.ServiceProvider.GetRequiredService<ICredentialStore>().FindAsync(Tenant, $"application-api-key:{applicationId}"));
    }

    [Fact]
    public async Task Every_refusal_is_the_same_401_whether_or_not_the_application_exists()
    {
        var (_, apiKey) = await CreateApplicationAsync(ReaderRole);
        var (revokedId, revokedKey) = await CreateApplicationAsync(ReaderRole);
        await _admin.PostAsync($"{ApplicationsRoute}/{revokedId}/api-key/revoke", null);
        var (disabledId, disabledKey) = await CreateApplicationAsync(ReaderRole);
        await SetEnabledAsync(disabledId, enabled: false);
        var segments = apiKey.Split('.');
        var wrongSecret = $"{segments[0]}.{segments[1]}.{Base64Url(RandomNumberGenerator.GetBytes(32))}";
        var unknownApplication = $"{segments[0]}.{Base64Url(Encoding.UTF8.GetBytes("no-such-application"))}.{segments[2]}";

        var refusals = new List<string>();
        foreach (var key in new[] { wrongSecret, unknownApplication, revokedKey, disabledKey, "not-a-key", $"{apiKey}.extra" })
        {
            var response = await SendWithKeyAsync(HttpMethod.Get, ApplicationsRoute, key);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
            refusals.Add($"{string.Join(',', response.Headers.WwwAuthenticate)}|{await response.Content.ReadAsStringAsync()}");
        }

        Assert.Single(refusals.Distinct(StringComparer.Ordinal));
        Assert.Equal(HttpStatusCode.OK, (await SendWithKeyAsync(HttpMethod.Get, ApplicationsRoute, apiKey)).StatusCode);
    }

    [Fact]
    public async Task A_bad_key_never_falls_back_to_the_callers_session_cookie()
    {
        Assert.Equal(HttpStatusCode.OK, (await _admin.GetAsync(ApplicationsRoute)).StatusCode);

        using var request = new HttpRequestMessage(HttpMethod.Get, ApplicationsRoute);
        request.Headers.Authorization = new AuthenticationHeaderValue("ApiKey", "not-a-key");

        Assert.Equal(HttpStatusCode.Unauthorized, (await _admin.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task A_key_cannot_be_exchanged_for_a_bearer_token()
    {
        var (_, apiKey) = await CreateApplicationAsync(ReaderRole);

        Assert.Equal(HttpStatusCode.Unauthorized, (await SendWithKeyAsync(HttpMethod.Get, "/_elsa/identity/token", apiKey)).StatusCode);
    }

    [Theory]
    [InlineData("""{ "displayName": " " }""")]
    [InlineData("""{ "displayName": "App", "roleIds": ["no-such-role"] }""")]
    public async Task An_invalid_application_is_refused_with_400(string body)
    {
        var response = await _admin.PostAsync(ApplicationsRoute, new StringContent(body, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task An_update_must_state_every_field()
    {
        var (applicationId, _) = await CreateApplicationAsync(ReaderRole);

        var response = await _admin.PutAsJsonAsync($"{ApplicationsRoute}/{applicationId}", new { displayName = "Renamed", roleIds = new[] { ReaderRole } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public void Every_application_route_declares_one_permission_disposition_with_a_catalogued_permission()
    {
        // Building the manifest validates each route: exactly one owner, authoring model and security disposition.
        // It also normalizes every route parameter to {param}.
        var applicationRoutes = new DefaultEndpointDataSource(_fixture.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.RoutePattern.RawText?.StartsWith(ApplicationsRoute, StringComparison.Ordinal) == true));
        var routes = new EndpointManifestBuilder([applicationRoutes]).Build().Entries;

        Assert.Equal(
            [
                ("DELETE", $"{ApplicationsRoute}/{{param}}", DefaultIdentityPermissionKeys.IdentityApplicationsManage),
                ("GET", ApplicationsRoute, DefaultIdentityPermissionKeys.IdentityApplicationsRead),
                ("GET", $"{ApplicationsRoute}/{{param}}", DefaultIdentityPermissionKeys.IdentityApplicationsRead),
                ("POST", ApplicationsRoute, DefaultIdentityPermissionKeys.IdentityApplicationsManage),
                ("POST", $"{ApplicationsRoute}/{{param}}/api-key/revoke", DefaultIdentityPermissionKeys.IdentityApplicationsManage),
                ("POST", $"{ApplicationsRoute}/{{param}}/api-key/rotate", DefaultIdentityPermissionKeys.IdentityApplicationsManage),
                ("PUT", $"{ApplicationsRoute}/{{param}}", DefaultIdentityPermissionKeys.IdentityApplicationsManage)
            ],
            routes
                .Select(entry => (Assert.Single(entry.Methods), entry.Route.Value, PermissionOf(entry)))
                .OrderBy(route => route.Item1, StringComparer.Ordinal)
                .ThenBy(route => route.Value, StringComparer.Ordinal));
        Assert.All(routes, entry => Assert.Equal("Elsa.Foundation.Identity.Api", entry.Owner));
        var permissions = new PermissionOwnershipValidator(_fixture.Services.GetServices<IPermissionContributor>()).Validate(routes);
        Assert.True(permissions.IsValid, string.Join(Environment.NewLine, permissions.Issues.Select(issue => $"{issue.Code}: {issue.Message}")));
    }

    private async Task<(string ApplicationId, string ApiKey)> CreateApplicationAsync(params string[] roleIds)
    {
        var response = await _admin.PostAsJsonAsync(ApplicationsRoute, new { displayName = "Integration", roleIds });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ReadIssuedAsync(response);
    }

    private async Task SetEnabledAsync(string applicationId, bool enabled)
    {
        var response = await _admin.PutAsJsonAsync($"{ApplicationsRoute}/{applicationId}", new { displayName = "Integration", roleIds = new[] { ReaderRole }, enabled });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<string?> ApiKeyStatusAsync(string applicationId)
    {
        using var application = JsonDocument.Parse(await _admin.GetStringAsync($"{ApplicationsRoute}/{applicationId}"));
        return application.RootElement.GetProperty("apiKeyStatus").GetString();
    }

    private async Task<HttpResponseMessage> SendWithKeyAsync(HttpMethod method, string route, string apiKey, object? body = null)
    {
        // A client without the admin's session cookie, sending the header the Elsa 3 API client sends.
        using var request = new HttpRequestMessage(method, route);
        request.Headers.Authorization = new AuthenticationHeaderValue("ApiKey", apiKey);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return await _fixture.Client.SendAsync(request);
    }

    private async Task<CredentialRecord> FindCredentialAsync(string applicationId)
    {
        await using var scope = _fixture.Services.CreateAsyncScope();
        return Assert.IsType<CredentialRecord>(await scope.ServiceProvider.GetRequiredService<ICredentialStore>()
            .FindAsync(Tenant, $"application-api-key:{applicationId}"));
    }

    private static async Task<(string ApplicationId, string ApiKey)> ReadIssuedAsync(HttpResponseMessage response)
    {
        using var issued = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return (issued.RootElement.GetProperty("application").GetProperty("id").GetString()!,
            issued.RootElement.GetProperty("apiKey").GetString()!);
    }

    private static async Task<string> ReadApiKeyAsync(HttpResponseMessage response)
    {
        using var issued = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return issued.RootElement.GetProperty("apiKey").GetString()!;
    }

    private static string PermissionOf(EndpointManifestEntry entry)
    {
        Assert.Equal(EndpointSecurityDispositionKind.Permission, entry.SecurityDisposition?.Kind);
        var policy = new PermissionPolicyCodec().Parse(entry.SecurityDisposition!.Value!);
        var permission = Assert.Single(policy.Descriptor!.Permissions);
        return new[] { DefaultIdentityPermissionKeys.IdentityApplicationsRead, DefaultIdentityPermissionKeys.IdentityApplicationsManage }
            .Single(candidate => PermissionKey.Normalize(candidate) == permission);
    }

    private static string[] Strings(JsonElement array) =>
        array.EnumerateArray().Select(value => value.GetString()!).Order(StringComparer.Ordinal).ToArray();

    private static string Base64Url(byte[] bytes) => System.Buffers.Text.Base64Url.EncodeToString(bytes);

    private static HashSet<string> Set(params string[] values) => new(values, StringComparer.Ordinal);
}
