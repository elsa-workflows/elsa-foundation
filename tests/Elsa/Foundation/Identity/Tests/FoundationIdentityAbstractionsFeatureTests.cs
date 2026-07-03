using Elsa.Foundation.Identity.Abstractions;
using Elsa.Foundation.Identity.Abstractions.Authentication;
using Elsa.Foundation.Identity.Abstractions.Authorization;
using Elsa.Foundation.Identity.Abstractions.Ownership;
using Elsa.Foundation.Identity.Abstractions.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Foundation.Identity.Tests;

public sealed class FoundationIdentityAbstractionsFeatureTests
{
    [Fact]
    public void RegistersContractDefaults()
    {
        var services = new ServiceCollection();

        new FoundationIdentityAbstractionsFeature().ConfigureServices(services);
        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IAuthenticationProviderResolver>());
        Assert.NotNull(provider.GetRequiredService<IPermissionCatalog>());
        Assert.NotNull(provider.GetRequiredService<IPermissionEvaluator>());
        Assert.NotNull(provider.GetRequiredService<IClaimsNormalizer>());
        Assert.NotNull(provider.GetRequiredService<IOwnershipModeProvider>());
        Assert.NotNull(provider.GetRequiredService<IEffectiveCapabilitiesResolver>());
        Assert.NotNull(provider.GetRequiredService<ISecurityDefaultGuardEvaluator>());
        Assert.IsType<RequirePermissionPolicyProvider>(provider.GetRequiredService<IAuthorizationPolicyProvider>());
    }

    [Fact]
    public async Task ProviderManagerComposesEnabledProviderModules()
    {
        var manager = new DefaultAuthenticationProviderResolver(
        [
            new TestProviderModule(new("external", "External", "external-oidc", ProviderCapabilities.ExternalOidcDefault, Enabled: true)),
            new TestProviderModule(new("disabled", "Disabled", "external-oidc", ProviderCapabilities.ExternalOidcDefault, Enabled: false)),
            new TestProviderModule(new("builtin", "Built-in", "openiddict", ProviderCapabilities.FoundationReference, Enabled: true, IsDefault: true))
        ]);

        var providers = await manager.ListAsync();
        var found = await manager.FindAsync("builtin");

        Assert.Collection(
            providers,
            x => Assert.Equal("builtin", x.Id),
            x => Assert.Equal("external", x.Id));
        Assert.Equal("builtin", found?.Id);
    }

    [Fact]
    public async Task ProviderManagerRequiresExplicitGlobalFallbackForTenantLookup()
    {
        var manager = new DefaultAuthenticationProviderResolver(
        [
            new TestProviderModule(new("entra", "Global Entra", "external-oidc", ProviderCapabilities.ExternalOidcDefault)),
            new TestProviderModule(new("entra", "Tenant Entra", "external-oidc", ProviderCapabilities.ExternalOidcDefault, TenantId: "tenant-a"))
        ]);

        var tenantProvider = await manager.FindAsync("entra", "tenant-a");
        var missingWithoutFallback = await manager.FindAsync("entra", "tenant-b");
        var missingWithFallback = await manager.FindAsync("entra", "tenant-b", allowGlobalFallback: true);

        Assert.Equal("Tenant Entra", tenantProvider?.DisplayName);
        Assert.Null(missingWithoutFallback);
        Assert.Equal("Global Entra", missingWithFallback?.DisplayName);
    }

    private sealed class TestProviderModule(AuthenticationProviderDescriptor descriptor) : IAuthenticationProviderModule
    {
        public string ProviderId => descriptor.Id;

        public string DisplayName => descriptor.DisplayName;

        public string Kind => descriptor.Kind;

        public ProviderCapabilities Capabilities => descriptor.Capabilities;

        public ValueTask<AuthenticationProviderDescriptor> DescribeAsync(CancellationToken cancellationToken = default) => ValueTask.FromResult(descriptor);
    }
}
