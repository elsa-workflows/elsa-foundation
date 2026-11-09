using Elsa.Integrations.Sdk.Harness;
using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Workflows;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

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

    [Fact]
    public async Task Runtime_secret_resolver_reads_ISecretValueResolver_on_each_call()
    {
        await using var harness = ConnectorHarness.Create();
        harness.Secrets.Add(harness.TenantId, "demo-token", "tok-1");
        IRuntimeSecretResolver resolver = new SecretValueRuntimeResolver(harness.Secrets);

        var first = await resolver.ResolveAsync(
            new RuntimeSecretResolutionRequest(harness.TenantId, new RuntimeSecretReference("demo-token")));
        var second = await resolver.ResolveAsync(
            new RuntimeSecretResolutionRequest(harness.TenantId, new RuntimeSecretReference("demo-token")));

        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
        Assert.Equal("tok-1", first.Value);
        Assert.Equal("tok-1", second.Value);
        Assert.Equal(2, harness.Secrets.ResolveCount);
    }
}
