using Elsa.Expressions.Core.Contracts;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Extensions;
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
        Assert.IsType<DefaultSecretResolver>(provider.GetRequiredService<ISecretResolver>());
        Assert.IsType<InMemorySecretRepository>(provider.GetRequiredService<ISecretRepository>());
        Assert.Contains(provider.GetServices<ISecretStore>(), x => x is EncryptedSecretStore);
        Assert.Contains(provider.GetServices<ISecretStore>(), x => x is ConfigurationSecretStore);
        Assert.Contains(provider.GetServices<IExpressionDescriptorProvider>(), x => x.GetDescriptors().Any(d => d.TypeName == "Secret"));
    }
}
