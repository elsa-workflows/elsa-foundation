using CShells.Features;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Extensions;
using Elsa.Secrets.Features;
using Elsa.Secrets.Options;
using Elsa.Secrets.Services;
using Elsa.Secrets.Stores;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Secrets.Tests;

public sealed class SecretsFeatureRegistrationTests
{
    [Fact]
    public void AddSecrets_Registers_Default_Services()
    {
        using var provider = new ServiceCollection().AddSecrets().BuildServiceProvider();

        Assert.IsType<DefaultSecretManager>(provider.GetRequiredService<ISecretManager>());
        Assert.IsType<DefaultSecretValueResolver>(provider.GetRequiredService<ISecretValueResolver>());
        Assert.IsType<InMemorySecretRepository>(provider.GetRequiredService<ISecretRepository>());
        Assert.Contains(provider.GetServices<ISecretStore>(), x => x is EncryptedSecretStore);
        Assert.Contains(provider.GetServices<ISecretStore>(), x => x is ConfigurationSecretStore);
        var secretDescriptor = provider.GetServices<IExpressionDescriptorProvider>()
            .SelectMany(x => x.GetDescriptors())
            .Single(x => x.TypeName == "Secret");
        Assert.Equal(ExpressionEditingMode.Reference, secretDescriptor.EditingMode);
    }

    [Fact]
    public void A_custom_value_resolver_swaps_in_without_touching_the_lifecycle_facade()
    {
        // The point of the W18 store-vs-resolver split: resolution and lifecycle are separately
        // overridable. A host replaces ISecretValueResolver only; ISecretManager keeps its default.
        var services = new ServiceCollection();
        services.AddSingleton<ISecretValueResolver, CustomValueResolver>();
        using var provider = services.AddSecrets().BuildServiceProvider();

        Assert.IsType<CustomValueResolver>(provider.GetRequiredService<ISecretValueResolver>());
        Assert.IsType<DefaultSecretManager>(provider.GetRequiredService<ISecretManager>());
    }

    [Fact]
    public void Scope_suggestions_follow_enabled_shell_module_composition()
    {
        using var enabled = ComposeShell(new ScopeCatalogFeature(), new SecretsFeature());
        using var disabled = ComposeShell(new SecretsFeature());

        Assert.Equal("billing", Assert.Single(enabled.GetRequiredService<ISecretScopeRegistry>().List()).Name);
        Assert.Empty(disabled.GetRequiredService<ISecretScopeRegistry>().List());
    }

    private sealed class CustomValueResolver : ISecretValueResolver
    {
        public ValueTask<ResolvedSecret> ResolveAsync(string tenantId, SecretReference reference, CancellationToken cancellationToken = default)
            => new(ResolvedSecret.Failure(SecretResolutionFailureCode.NotFound, "custom"));
    }

    [Fact]
    public void Secrets_feature_builds_the_key_ring_from_its_settings()
    {
        using var provider = Compose(new SecretsFeature
        {
            EncryptionKey = "first-key",
            Keys = new Dictionary<string, string> { ["2026-10"] = "rotated-key" },
            ActiveKeyId = "2026-10"
        });

        var keyRing = provider.GetRequiredService<ISecretKeyRing>();
        Assert.Equal("2026-10", keyRing.ActiveKey.KeyId);
        Assert.True(keyRing.TryGetKey(SecretEncryptionKey.LegacyKeyId, out _));
    }

    [Fact]
    public void Secrets_feature_with_only_an_encryption_key_can_protect_values()
    {
        using var provider = Compose(new SecretsFeature { EncryptionKey = "only-key" });

        var protector = provider.GetRequiredService<ISecretValueProtector>();
        Assert.Equal("value", protector.Unprotect(protector.Protect("value")));
        Assert.Equal(SecretEncryptionKey.LegacyKeyId, provider.GetRequiredService<ISecretKeyRing>().ActiveKey.KeyId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(" ")]
    public void Secrets_feature_without_key_settings_keeps_the_host_configured_keys(string? unset)
    {
        // A host that configures SecretsOptions itself must not have its keys blanked by a feature whose settings are unset.
        using var provider = Compose(
            new SecretsFeature { EncryptionKey = unset, ActiveKeyId = unset },
            options =>
            {
                options.EncryptionKey = "host-first-key";
                options.Keys.Add(new SecretEncryptionKeyOptions { KeyId = "host-rotated", Key = "host-rotated-key" });
                options.ActiveKeyId = "host-rotated";
            });

        var keyRing = provider.GetRequiredService<ISecretKeyRing>();
        Assert.Equal("host-rotated", keyRing.ActiveKey.KeyId);
        Assert.True(keyRing.TryGetKey(SecretEncryptionKey.LegacyKeyId, out _));
    }

    private static ServiceProvider Compose(SecretsFeature feature, Action<SecretsOptions>? hostOptions = null)
    {
        var services = new ServiceCollection();
        if (hostOptions is not null)
            services.Configure(hostOptions);

        feature.ConfigureServices(services);
        return services.BuildServiceProvider();
    }

    private static ServiceProvider ComposeShell(params IShellFeature[] features)
    {
        var services = new ServiceCollection();
        foreach (var feature in features)
            feature.ConfigureServices(services);

        return services.BuildServiceProvider();
    }

    private sealed class ScopeCatalogFeature : IShellFeature
    {
        public void ConfigureServices(IServiceCollection services) =>
            services.TryAddEnumerable(ServiceDescriptor.Singleton<ISecretScopeProvider, BillingScopeProvider>());
    }

    private sealed class BillingScopeProvider : ISecretScopeProvider
    {
        public IEnumerable<SecretScopeDescriptor> GetScopes() =>
        [
            new("billing", "Billing")
        ];
    }
}
