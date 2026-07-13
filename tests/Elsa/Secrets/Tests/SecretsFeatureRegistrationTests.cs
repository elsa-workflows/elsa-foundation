using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Extensions;
using Elsa.Secrets.Features;
using Elsa.Secrets.Services;
using Elsa.Secrets.Stores;
using Microsoft.Extensions.DependencyInjection;
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

    private sealed class CustomValueResolver : ISecretValueResolver
    {
        public ValueTask<ResolvedSecret> ResolveAsync(SecretReference reference, CancellationToken cancellationToken = default)
            => new(ResolvedSecret.Failure(SecretResolutionFailureCode.NotFound, "custom"));
    }

    [Fact]
    public void Secrets_feature_registers_secret_manager()
    {
        var services = new ServiceCollection();
        var feature = new SecretsFeature();

        feature.ConfigureServices(services);
        using var provider = services.BuildServiceProvider();

        // MD-10 (§2.23.1): construct the feature class itself and prove its wiring, complementing the AddSecrets() extension test above.
        provider.GetRequiredService<ISecretManager>();
    }
}
