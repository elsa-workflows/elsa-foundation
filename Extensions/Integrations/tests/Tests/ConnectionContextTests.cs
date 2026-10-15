using Elsa.Integrations.Sdk;
using Elsa.Integrations.Sdk.Connections;
using Elsa.Integrations.Sdk.Harness;
using Elsa.Secrets.Core.Models;

namespace Elsa.Integrations.Tests;

public sealed class ConnectionContextTests
{
    [Fact]
    public async Task Resolves_a_tenant_connection_and_reads_secrets_uncached()
    {
        await using var harness = ConnectorHarness.Create();
        harness.Connections.Add(Snapshot(harness.TenantId, "demo"));
        harness.Secrets.Add(harness.TenantId, "demo-token", "tok-1");

        await using var handle = await harness.ConnectionContext.ResolveAsync(new ConnectionReference("demo", "http"));
        Assert.Equal("demo", handle.ConnectionName);
        Assert.Equal("http", handle.ConnectionType);
        Assert.Equal(ConnectionAuthSchemes.Bearer, handle.AuthScheme);

        var first = await handle.GetSensitiveAsync("accessToken");
        var second = await handle.GetSensitiveAsync("accessToken");
        Assert.Equal("tok-1", first.Value);
        Assert.Equal("tok-1", second.Value);
        Assert.Equal(2, harness.Secrets.ResolveCount);
        Assert.Equal(SensitiveValue.Redacted, first.ToString());
    }

    [Fact]
    public async Task Missing_connection_is_unavailable()
    {
        await using var harness = ConnectorHarness.Create();
        var ex = await Assert.ThrowsAsync<ConnectionUnavailableException>(() =>
            harness.ConnectionContext.ResolveAsync(new ConnectionReference("missing")).AsTask());
        Assert.Equal("missing", ex.ConnectionName);
        Assert.False(ex.IsRetryable);
    }

    [Fact]
    public async Task Wrong_type_is_unavailable()
    {
        await using var harness = ConnectorHarness.Create();
        harness.Connections.Add(Snapshot(harness.TenantId, "demo"));
        var ex = await Assert.ThrowsAsync<ConnectionUnavailableException>(() =>
            harness.ConnectionContext.ResolveAsync(new ConnectionReference("demo", "smtp")).AsTask());
        Assert.Contains("smtp", ex.Message);
    }

    [Fact]
    public async Task Non_tenant_owner_is_unavailable()
    {
        await using var harness = ConnectorHarness.Create();
        harness.Connections.Add(Snapshot(harness.TenantId, "demo") with { Owner = "user" });
        var ex = await Assert.ThrowsAsync<ConnectionUnavailableException>(() =>
            harness.ConnectionContext.ResolveAsync(new ConnectionReference("demo")).AsTask());
        Assert.Contains(ConnectionOwners.Tenant, ex.Message);
    }

    private static ConnectionSnapshot Snapshot(string tenantId, string name) => new()
    {
        TenantId = tenantId,
        Name = name,
        TypeName = "http",
        AuthScheme = ConnectionAuthSchemes.Bearer,
        SecretFields = new Dictionary<string, SecretReference>(StringComparer.Ordinal)
        {
            ["accessToken"] = new("demo-token")
        }
    };
}
