using Elsa.Integrations.Sdk.Harness;
using Elsa.Integrations.Tests.Support;
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
    public async Task Credential_input_resolves_through_runtime_secret_resolver_on_each_activation()
    {
        await using var harness = ConnectorHarness.Create();
        harness.Secrets.Add(harness.TenantId, "demo-token", "tok-1");
        IRuntimeSecretResolver resolver = new SecretValueRuntimeResolver(harness.Secrets);

        var first = await ActivateAsync(resolver, harness.TenantId);
        var second = await ActivateAsync(resolver, harness.TenantId);

        Assert.Equal("tok-1", first.Token);
        Assert.Equal("tok-1", second.Token);
        Assert.Equal(2, harness.Secrets.ResolveCount);
    }

    private static async Task<SampleCredentialActivity> ActivateAsync(IRuntimeSecretResolver resolver, string tenantId)
    {
        var resolution = await resolver.ResolveAsync(
            new RuntimeSecretResolutionRequest(tenantId, new RuntimeSecretReference("demo-token")));
        Assert.True(resolution.Succeeded);
        return new SampleCredentialActivity { Token = resolution.Value };
    }
}
