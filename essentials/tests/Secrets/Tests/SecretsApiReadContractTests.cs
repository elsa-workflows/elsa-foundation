using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Services;
using Elsa.Secrets.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Elsa.Secrets.Tests;

public sealed class SecretsApiReadContractTests(ITestOutputHelper output)
{
    private static readonly IReadOnlySet<string> ReadEndpoints = new HashSet<string>(StringComparer.Ordinal)
    {
        "GET /secrets",
        "GET /secrets/descriptors",
        "POST /secrets/picker",
        "GET /secrets/{param}"
    };

    [Fact]
    public async Task List_preserves_filters_paging_tenant_isolation_and_safe_metadata()
    {
        await using var host = await SecretsCanaryHost.StartMigratedAsync(output);
        const string marker = "read-contract-sensitive-value";

        using (var create = await host.Client.SendAsync(JsonRequest(
                   HttpMethod.Post,
                   "/secrets",
                   "write",
                   JsonSerializer.Serialize(new { name = "read-contract-secret", displayName = "Tenant A", value = marker, metadata = new { providerMarker = marker }}))))
        {
            Assert.Equal(HttpStatusCode.Created, create.StatusCode);
            await AssertSafeAsync(create, marker);
        }

        using var response = await host.Client.SendAsync(Request(
            HttpMethod.Get,
            "/secrets?search=read-contract&typeNames=text&typeNames=rsa-key&storeNames=encrypted&status=active&activeOnly=true&page=-1&pageSize=999",
            "read"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        await AssertSafeAsync(response, marker);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var items = document.RootElement.GetProperty("items");
        Assert.Contains(items.EnumerateArray(), item => item.GetProperty("name").GetString() == "read-contract-secret");
        Assert.DoesNotContain(items.EnumerateArray(), item => item.TryGetProperty("value", out _));
        Assert.DoesNotContain(items.EnumerateArray(), item => item.TryGetProperty("configurationKey", out _));
    }

    [Fact]
    public async Task Get_and_picker_are_tenant_scoped_and_deleted_records_are_not_discoverable()
    {
        await using var host = await SecretsCanaryHost.StartMigratedAsync(output);
        const string name = "same-name-read-contract";

        using (var createA = await host.Client.SendAsync(JsonRequest(
                   HttpMethod.Post,
                   "/secrets",
                   "write",
                   JsonSerializer.Serialize(new { name, displayName = "Tenant A secret", value = "tenant-a-value" }))))
            Assert.Equal(HttpStatusCode.Created, createA.StatusCode);

        using (var createB = await host.Client.SendAsync(JsonRequest(
                   HttpMethod.Post,
                   "/secrets",
                   "write|tenant-beta",
                   JsonSerializer.Serialize(new { name, displayName = "Tenant B secret", value = "tenant-b-value" }))))
            Assert.Equal(HttpStatusCode.Created, createB.StatusCode);

        using var getA = await host.Client.SendAsync(Request(HttpMethod.Get, $"/secrets/{name}", "read"));
        using var getB = await host.Client.SendAsync(Request(HttpMethod.Get, $"/secrets/{name}", "read|tenant-beta"));
        Assert.Equal(HttpStatusCode.OK, getA.StatusCode);
        Assert.Equal(HttpStatusCode.OK, getB.StatusCode);

        using var aDocument = JsonDocument.Parse(await getA.Content.ReadAsStringAsync());
        using var bDocument = JsonDocument.Parse(await getB.Content.ReadAsStringAsync());
        Assert.Equal("Tenant A secret", aDocument.RootElement.GetProperty("displayName").GetString());
        Assert.Equal("Tenant B secret", bDocument.RootElement.GetProperty("displayName").GetString());
        Assert.NotEqual(
            aDocument.RootElement.GetProperty("tenantId").GetString(),
            bDocument.RootElement.GetProperty("tenantId").GetString());

        using var picker = await host.Client.SendAsync(JsonRequest(
            HttpMethod.Post,
            "/secrets/picker",
            "read",
            JsonSerializer.Serialize(new { search = name, typeNames = new[] { "text" }, storeNames = new[] { "encrypted" }, activeOnly = true })));
        Assert.Equal(HttpStatusCode.OK, picker.StatusCode);
        using var pickerDocument = JsonDocument.Parse(await picker.Content.ReadAsStringAsync());
        Assert.True(pickerDocument.RootElement.GetProperty("canCreateInline").GetBoolean());
        Assert.All(pickerDocument.RootElement.GetProperty("items").EnumerateArray(), item =>
            Assert.Equal(SecretsCanaryHost.AlphaTenant, item.GetProperty("tenantId").GetString()));

        using (var delete = await host.Client.SendAsync(Request(HttpMethod.Delete, $"/secrets/{name}", "delete")))
            Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        using var missing = await host.Client.SendAsync(Request(HttpMethod.Get, $"/secrets/{name}", "read"));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Descriptors_are_read_authorized_but_do_not_require_a_tenant_claim()
    {
        await using var host = await SecretsCanaryHost.StartMigratedAsync(output);

        using var response = await host.Client.SendAsync(Request(HttpMethod.Get, "/secrets/descriptors", "read|no-tenant"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.NotEmpty(document.RootElement.GetProperty("types").EnumerateArray());
        Assert.NotEmpty(document.RootElement.GetProperty("stores").EnumerateArray());
        Assert.Empty(document.RootElement.GetProperty("scopes").EnumerateArray());

        using var forbidden = await host.Client.SendAsync(Request(HttpMethod.Get, "/secrets/descriptors", "write|no-tenant"));
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
    }

    [Fact]
    public async Task Descriptors_include_sorted_module_scope_suggestions_and_contributor_context()
    {
        await using var host = await SecretsCanaryHost.StartMigratedAsync(output, services =>
        {
            services.AddSingleton<ISecretScopeProvider, WorkflowScopeProvider>();
            services.AddSingleton<ISecretScopeProvider, ConnectionsScopeProvider>();
        });

        using var response = await host.Client.SendAsync(Request(HttpMethod.Get, "/secrets/descriptors", "read|no-tenant"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var scopes = document.RootElement.GetProperty("scopes").EnumerateArray().ToArray();
        Assert.Equal(["connections", "Workflow"], scopes.Select(scope => scope.GetProperty("name").GetString()));
        Assert.Equal("Connections", scopes[0].GetProperty("displayName").GetString());
        Assert.Equal(JsonValueKind.Null, scopes[0].GetProperty("source").ValueKind);
        Assert.Equal("Workflow secrets", scopes[1].GetProperty("description").GetString());
        Assert.Equal("Workflows", scopes[1].GetProperty("source").GetString());
    }

    [Fact]
    public async Task Create_preserves_an_unregistered_custom_scope_when_the_catalog_is_empty()
    {
        await using var host = await SecretsCanaryHost.StartMigratedAsync(output);
        const string scope = "My.CustomScope";

        using var create = await host.Client.SendAsync(JsonRequest(
            HttpMethod.Post,
            "/secrets",
            "write",
            JsonSerializer.Serialize(new { name = "custom-scope-secret", value = "custom-scope-value", scope })));

        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        using var document = JsonDocument.Parse(await create.Content.ReadAsStringAsync());
        Assert.Equal(scope, document.RootElement.GetProperty("scope").GetString());
    }

    [Fact]
    public async Task Scope_catalog_does_not_change_runtime_matching_or_existing_custom_and_unset_scopes()
    {
        var repository = new InMemorySecretRepository();

        await using (var contributedHost = await SecretsCanaryHost.StartMigratedAsync(output, services =>
                     {
                         services.AddSingleton<ISecretRepository>(repository);
                         services.AddSingleton<ISecretScopeProvider, ConnectionsScopeProvider>();
                     }))
        {
            await CreateSecretAsync(contributedHost, "contributed-scope-secret", "contributed-value", "connections");
            await CreateSecretAsync(contributedHost, "custom-scope-secret", "custom-value", "Legacy.TaskRunner");
            await CreateSecretAsync(contributedHost, "unset-scope-secret", "unset-value", scope: null);

            var matching = await ResolveAsync(contributedHost, "contributed-scope-secret", "CONNECTIONS");
            Assert.True(matching.Succeeded);
            Assert.Equal("contributed-value", matching.Value);

            var mismatch = await ResolveAsync(contributedHost, "contributed-scope-secret", "different-scope");
            Assert.False(mismatch.Succeeded);
            Assert.Equal(SecretResolutionFailureCode.ScopeMismatch, mismatch.FailureCode);
        }

        await using var providerRemovedHost = await SecretsCanaryHost.StartMigratedAsync(
            output,
            services => services.AddSingleton<ISecretRepository>(repository));

        Assert.Empty(providerRemovedHost.Services.GetRequiredService<ISecretScopeRegistry>().List());

        var contributedScopeStillResolves = await ResolveAsync(providerRemovedHost, "contributed-scope-secret", "connections");
        Assert.True(contributedScopeStillResolves.Succeeded);
        Assert.Equal("contributed-value", contributedScopeStillResolves.Value);

        var customScopeStillResolves = await ResolveAsync(providerRemovedHost, "custom-scope-secret", "Legacy.TaskRunner");
        Assert.True(customScopeStillResolves.Succeeded);
        Assert.Equal("custom-value", customScopeStillResolves.Value);

        var unsetScopeStillResolves = await ResolveAsync(providerRemovedHost, "unset-scope-secret", scope: null);
        Assert.True(unsetScopeStillResolves.Succeeded);
        Assert.Equal("unset-value", unsetScopeStillResolves.Value);
    }

    [Fact]
    public async Task List_and_get_preserve_singular_filters_and_lifecycle_visibility()
    {
        await using var host = await SecretsCanaryHost.StartMigratedAsync(output);

        using var revokedList = await host.Client.SendAsync(Request(
            HttpMethod.Get,
            "/secrets?typeName=text&storeName=encrypted&status=revoked&page=0&pageSize=1",
            "read"));
        Assert.Equal(HttpStatusCode.OK, revokedList.StatusCode);
        using var revokedDocument = JsonDocument.Parse(await revokedList.Content.ReadAsStringAsync());
        Assert.Contains(
            revokedDocument.RootElement.GetProperty("items").EnumerateArray(),
            item => item.GetProperty("name").GetString() == "revoked.secret");

        using var expired = await host.Client.SendAsync(Request(HttpMethod.Get, "/secrets/expired.secret", "read"));
        Assert.Equal(HttpStatusCode.OK, expired.StatusCode);
        using var expiredDocument = JsonDocument.Parse(await expired.Content.ReadAsStringAsync());
        Assert.Equal("active", expiredDocument.RootElement.GetProperty("status").GetString());

        using var activeOnly = await host.Client.SendAsync(Request(
            HttpMethod.Get,
            "/secrets?search=expired.secret&activeOnly=true",
            "read"));
        using var activeOnlyDocument = JsonDocument.Parse(await activeOnly.Content.ReadAsStringAsync());
        Assert.Empty(activeOnlyDocument.RootElement.GetProperty("items").EnumerateArray());

        using var deleted = await host.Client.SendAsync(Request(HttpMethod.Get, "/secrets/deleted.secret", "read"));
        Assert.Equal(HttpStatusCode.NotFound, deleted.StatusCode);
    }

    private static async Task CreateSecretAsync(SecretsCanaryHost host, string name, string value, string? scope)
    {
        var request = new Dictionary<string, object?>
        {
            ["name"] = name,
            ["value"] = value
        };
        if (scope is not null)
            request["scope"] = scope;

        using var response = await host.Client.SendAsync(JsonRequest(
            HttpMethod.Post,
            "/secrets",
            "write",
            JsonSerializer.Serialize(request)));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    private static async Task<ResolvedSecret> ResolveAsync(SecretsCanaryHost host, string name, string? scope)
    {
        await using var requestScope = host.Services.CreateAsyncScope();
        var resolver = requestScope.ServiceProvider.GetRequiredService<ISecretValueResolver>();
        return await resolver.ResolveAsync(
            SecretsCanaryHost.AlphaTenant,
            new SecretReference(name, SecretTypeNames.Text, scope));
    }

    private static HttpRequestMessage Request(HttpMethod method, string path, string? identity)
    {
        var request = new HttpRequestMessage(method, path);
        if (identity is not null)
            request.Headers.TryAddWithoutValidation(SecretsCanaryHost.IdentityHeader, identity);
        return request;
    }

    private static HttpRequestMessage JsonRequest(HttpMethod method, string path, string? identity, string body)
    {
        var request = Request(method, path, identity);
        request.Content = JsonContent.Create(JsonDocument.Parse(body).RootElement);
        return request;
    }

    private static async Task AssertSafeAsync(HttpResponseMessage response, string marker)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(marker, body, StringComparison.Ordinal);
        Assert.DoesNotContain(
            response.Headers.Concat(response.Content.Headers).SelectMany(header => header.Value),
            value => value.Contains(marker, StringComparison.Ordinal));
    }

    private sealed class ConnectionsScopeProvider : ISecretScopeProvider
    {
        public IEnumerable<SecretScopeDescriptor> GetScopes() =>
        [
            new("connections", "Connections", "Connection credentials")
        ];
    }

    private sealed class WorkflowScopeProvider : ISecretScopeProvider
    {
        public IEnumerable<SecretScopeDescriptor> GetScopes() =>
        [
            new("Workflow", "Workflow", "Workflow secrets", "Workflows")
        ];
    }
}
