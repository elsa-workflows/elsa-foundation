using System.Security.Cryptography;
using Elsa.Agent.Specialists.Anthropic;
using Elsa.Agent.Specialists.AzureOpenAI;
using Elsa.Agent.Specialists.Core.Contracts;
using Elsa.Connections.Core.Contracts;
using Elsa.Connections.Core.Models;
using Elsa.Connections.Features;
using Elsa.Secrets.Features;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Agent.Specialists.Tests.Providers;

public sealed class SpecialistConnectionCompositionTests
{
    [Fact]
    public async Task Model_metadata_and_opening_use_the_actual_Connections_registry_and_Secrets_path()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<IPersistenceAccessContextAccessor>(_ => new SpecialistConnectionProviderTests.Access());
        new SecretsFeature { EncryptionKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) }.ConfigureServices(services);
        new ConnectionsFeature().ConfigureServices(services);
        new AnthropicSpecialistsFeature().ConfigureServices(services);
        new AzureOpenAISpecialistsFeature().ConfigureServices(services);
        await using var root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        await using var scope = root.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IConnectionManager>();
        foreach (var factory in scope.ServiceProvider.GetServices<ISpecialistModelClientFactory>())
        {
            await manager.CreateAsync("tenant", new()
            {
                Name = "model-connection", TypeName = "specialist-" + factory.ProviderKey, AuthScheme = HttpAuthSchemes.ApiKey,
                Settings = new SpecialistConnectionProviderTests.Resolved(factory.ProviderKey).Values,
                SecretFields = new Dictionary<string, ConnectionSecretFieldInput>
                {
                    [HttpConnectionSecretFields.ApiKey] = new() { Value = "fixture-not-a-real-credential" }
                }
            });
            var model = SpecialistConnectionProviderTests.Model(factory.ProviderKey);
            var source = scope.ServiceProvider.GetServices<ISpecialistModelCapabilitiesSource>().Single(x => x.ProviderKey == factory.ProviderKey);
            var metadata = await source.GetCapabilitiesAsync(model);
            await using var lease = await factory.OpenAsync(SpecialistConnectionProviderTests.Request(model, metadata));
            Assert.True(lease.ConnectionVersion > 0);
            Assert.Equal(metadata.Fingerprint, lease.CapabilitiesFingerprint);
            await manager.DeleteAsync("tenant", "model-connection");
        }
    }
}
