using Elsa.Foundation.Identity.Abstractions.Iam;
using Elsa.Foundation.Identity.Abstractions.Ownership;

namespace Elsa.Foundation.Identity.Tests;

public sealed class IamContractsTests
{
    [Fact]
    public async Task ProviderConfigurationStoreRequiresExplicitGlobalFallback()
    {
        var fixture = new TestProviderConfigurationStore();
        fixture.Global["entra"] = new(
            "entra",
            null,
            "external-oidc",
            true,
            true,
            ProviderCapabilities.ExternalOidcDefault,
            new Dictionary<string, string>());
        fixture.Tenant["tenant-a:entra"] = new(
            "entra",
            "tenant-a",
            "external-oidc",
            true,
            true,
            ProviderCapabilities.ExternalOidcDefault,
            new Dictionary<string, string>());
        IProviderConfigurationStore store = fixture;

        var tenantConfiguration = await store.FindEffectiveAsync("tenant-a", "entra");
        var missingWithoutFallback = await store.FindEffectiveAsync("tenant-b", "entra");
        var missingWithFallback = await store.FindEffectiveAsync("tenant-b", "entra", allowGlobalFallback: true);

        Assert.Equal("tenant-a", tenantConfiguration?.TenantId);
        Assert.Null(missingWithoutFallback);
        Assert.Null(missingWithFallback?.TenantId);
    }

    [Fact]
    public void Bounded_page_request_rejects_unbounded_inputs()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new IamPageRequest(skip: -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new IamPageRequest(take: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new IamPageRequest(take: IamPageRequest.MaximumTake + 1));
    }

    [Fact]
    public void Bounded_page_preserves_total_count_and_rejects_impossible_results()
    {
        var page = new IamPage<string>(["a", "b"], totalCount: 3);

        Assert.Equal(["a", "b"], page.Items);
        Assert.Equal(3L, page.TotalCount);
        Assert.Throws<ArgumentOutOfRangeException>(() => new IamPage<string>(["a"], totalCount: 0));
    }

    private sealed class TestProviderConfigurationStore : IProviderConfigurationStore
    {
        public Dictionary<string, ProviderConfigurationRecord> Global { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, ProviderConfigurationRecord> Tenant { get; } = new(StringComparer.OrdinalIgnoreCase);

        public ValueTask<ProviderConfigurationRecord?> FindGlobalAsync(string provider, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Global.GetValueOrDefault(provider));

        public ValueTask<ProviderConfigurationRecord?> FindForTenantAsync(string tenantId, string provider, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Tenant.GetValueOrDefault($"{tenantId}:{provider}"));

        public ValueTask SaveAsync(ProviderConfigurationRecord configuration, CancellationToken cancellationToken = default)
        {
            if (configuration.TenantId is null)
                Global[configuration.Provider] = configuration;
            else
                Tenant[$"{configuration.TenantId}:{configuration.Provider}"] = configuration;

            return ValueTask.CompletedTask;
        }
    }
}
