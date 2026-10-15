using Elsa.Integrations.Sdk;
using Elsa.Integrations.Sdk.Connections;
using Elsa.Integrations.Sdk.Harness;
using Elsa.Integrations.Sdk.Connections.Schemes;
using Elsa.Secrets.Core.Models;

namespace Elsa.Integrations.Tests;

public sealed class SchemeAuthenticatorTests
{
    [Fact]
    public async Task Api_key_header_and_basic_auth_apply()
    {
        await using var harness = ConnectorHarness.Create();
        harness.Secrets.Add(harness.TenantId, "key", "k-secret");
        harness.Secrets.Add(harness.TenantId, "pw", "p-secret");

        harness.Connections.Add(new ConnectionSnapshot
        {
            TenantId = harness.TenantId,
            Name = "keys",
            TypeName = "http",
            AuthScheme = ConnectionAuthSchemes.ApiKey,
            SecretFields = new Dictionary<string, SecretReference> { ["apiKey"] = new("key") }
        });
        await using (var handle = await harness.ConnectionContext.ResolveAsync(new ConnectionReference("keys")))
            await handle.HttpClient.GetAsync("https://api.example.com/v1");
        Assert.Equal("k-secret", harness.Http.LastRequest?.Headers.GetValues(ApiKeyConnectionAuthenticator.DefaultHeaderName).Single());

        harness.Connections.Add(new ConnectionSnapshot
        {
            TenantId = harness.TenantId,
            Name = "basic",
            TypeName = "http",
            AuthScheme = ConnectionAuthSchemes.Basic,
            Settings = new Dictionary<string, string> { [BasicConnectionAuthenticator.UsernameSetting] = "alice" },
            SecretFields = new Dictionary<string, SecretReference> { ["password"] = new("pw") }
        });
        await using (var handle = await harness.ConnectionContext.ResolveAsync(new ConnectionReference("basic")))
            await handle.HttpClient.GetAsync("https://api.example.com/v1");
        Assert.Equal("Basic", harness.Http.LastRequest?.Headers.Authorization?.Scheme);
        Assert.Equal(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("alice:p-secret")), harness.Http.LastRequest?.Headers.Authorization?.Parameter);
    }
}
