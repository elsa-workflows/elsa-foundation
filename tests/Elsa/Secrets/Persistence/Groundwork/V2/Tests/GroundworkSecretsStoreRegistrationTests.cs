using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Persistence.Groundwork.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Secrets.Persistence.Groundwork.V2.Tests;

public sealed class GroundworkSecretsStoreRegistrationTests
{
    [Fact]
    public void Registration_records_its_provider_local_backend_name()
    {
        var services = new ServiceCollection();

        services.AddGroundworkSecretsStore();

        Assert.Equal("groundwork", Assert.Single(services
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<SecretRepositoryBackend>()).Name);
    }

    [Fact]
    public void Registration_refuses_a_prior_entity_framework_backend()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new SecretRepositoryBackend("entity-framework"));
        var exception = Assert.Throws<InvalidOperationException>(() => services.AddGroundworkSecretsStore());
        Assert.Contains("entity-framework", exception.Message, StringComparison.Ordinal);
        Assert.Contains("groundwork", exception.Message, StringComparison.Ordinal);
    }
}
