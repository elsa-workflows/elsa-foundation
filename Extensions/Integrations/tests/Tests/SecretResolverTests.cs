using Elsa.Integrations.Sdk.Harness;
using Elsa.Secrets.Core.Models;

namespace Elsa.Integrations.Tests;

public sealed class SecretResolverTests
{
    [Fact]
    public async Task Harness_resolver_is_uncached_and_tenant_scoped()
    {
        await using var harness = ConnectorHarness.Create();
        harness.Secrets.Add(harness.TenantId, "demo-token", "tok-1");

        var first = await harness.Secrets.ResolveAsync(harness.TenantId, new SecretReference("demo-token"));
        var second = await harness.Secrets.ResolveAsync(harness.TenantId, new SecretReference("demo-token"));
        var otherTenant = await harness.Secrets.ResolveAsync("other", new SecretReference("demo-token"));

        Assert.True(first.Succeeded);
        Assert.Equal("tok-1", first.Value);
        Assert.Equal("tok-1", second.Value);
        Assert.Equal(3, harness.Secrets.ResolveCount);
        Assert.False(otherTenant.Succeeded);
        Assert.Equal(SecretResolutionFailureCode.NotFound, otherTenant.FailureCode);
    }
}
