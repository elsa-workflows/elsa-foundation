using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Persistence.Groundwork.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Secrets.Persistence.Groundwork.V2.Tests;

public sealed class GroundworkSecretsStoreRegistrationTests
{
    [Fact]
    public void Registration_refuses_a_prior_entity_framework_backend()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new SecretRepositoryBackend(SecretRepositoryBackend.EntityFramework));
        var exception = Assert.Throws<InvalidOperationException>(() => services.AddGroundworkSecretsStore());
        Assert.Contains(SecretRepositoryBackend.EntityFramework, exception.Message, StringComparison.Ordinal);
        Assert.Contains(SecretRepositoryBackend.Groundwork, exception.Message, StringComparison.Ordinal);
    }
}
