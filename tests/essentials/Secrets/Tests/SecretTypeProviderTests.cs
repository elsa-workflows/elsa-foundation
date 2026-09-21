using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Types;
using Xunit;

namespace Elsa.Secrets.Tests;

public sealed class SecretTypeProviderTests
{
    [Fact]
    public async Task Text_Encrypted_Secret_Requires_Value()
    {
        var provider = new TextSecretTypeProvider();

        var result = await provider.ValidateCreateAsync(new CreateSecretRequest { StoreName = SecretStoreNames.Encrypted });

        Assert.False(result.Succeeded);
    }

    [Fact]
    public async Task Text_Configuration_Secret_Requires_Key()
    {
        var provider = new TextSecretTypeProvider();

        var result = await provider.ValidateCreateAsync(new CreateSecretRequest { StoreName = SecretStoreNames.Configuration });

        Assert.False(result.Succeeded);
    }

    [Fact]
    public void BuiltIn_Descriptors_Declare_Compatible_Stores()
    {
        var descriptors = new[]
        {
            new TextSecretTypeProvider().Descriptor,
            new RsaKeySecretTypeProvider().Descriptor,
            new X509CertificateSecretTypeProvider().Descriptor
        };

        Assert.All(descriptors, descriptor =>
        {
            Assert.Contains(SecretStoreNames.Encrypted, descriptor.SupportedStoreNames);
            Assert.Contains(SecretStoreNames.Configuration, descriptor.SupportedStoreNames);
        });
    }
}
