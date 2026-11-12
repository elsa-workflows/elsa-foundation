using System.Security.Cryptography;
using Elsa.Agent.Anthropic;
using Elsa.Agent.AzureOpenAI;
using Elsa.Agent.OpenAI;
using Elsa.Agent.Core.Contracts;
using Elsa.Connections.Core.Contracts;
using Elsa.Connections.Core.Models;
using Elsa.Connections.Features;
using Elsa.Secrets.Features;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Agent.Tests.Providers;

public sealed class AgentConnectionCompositionTests
{
    [Fact]
    public async Task Model_metadata_and_opening_use_the_actual_Connections_registry_and_Secrets_path()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<IPersistenceAccessContextAccessor>(_ => new AgentConnectionProviderTests.Access());
        new SecretsFeature { EncryptionKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)) }.ConfigureServices(services);
        new ConnectionsFeature().ConfigureServices(services);
        new AnthropicAgentsFeature().ConfigureServices(services);
        new AzureOpenAIAgentsFeature().ConfigureServices(services);
        new OpenAIAgentsFeature().ConfigureServices(services);
        await using var root = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        await using var scope = root.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<IConnectionManager>();
        Assert.Equal(new[] { "anthropic", "azure-openai", "openai" },
            scope.ServiceProvider.GetServices<IAgentModelClientFactory>().Select(x => x.ProviderKey).Order(StringComparer.Ordinal));
        Assert.Equal(new[] { "anthropic", "azure-openai", "openai" },
            scope.ServiceProvider.GetServices<IAgentModelCapabilitiesSource>().Select(x => x.ProviderKey).Order(StringComparer.Ordinal));
        foreach (var factory in scope.ServiceProvider.GetServices<IAgentModelClientFactory>())
        {
            await manager.CreateAsync("tenant", new()
            {
                Name = "model-connection", TypeName = "agent-" + factory.ProviderKey, AuthScheme = HttpAuthSchemes.ApiKey,
                Settings = new AgentConnectionProviderTests.Resolved(factory.ProviderKey).Values,
                SecretFields = new Dictionary<string, ConnectionSecretFieldInput>
                {
                    [HttpConnectionSecretFields.ApiKey] = new() { Value = "fixture-not-a-real-credential" }
                }
            });
            var model = AgentConnectionProviderTests.Model(factory.ProviderKey);
            var source = scope.ServiceProvider.GetServices<IAgentModelCapabilitiesSource>().Single(x => x.ProviderKey == factory.ProviderKey);
            var metadata = await source.GetCapabilitiesAsync(model);
            await using var lease = await factory.OpenAsync(AgentConnectionProviderTests.Request(model, metadata));
            Assert.True(lease.ConnectionVersion > 0);
            Assert.Equal(metadata.Fingerprint, lease.CapabilitiesFingerprint);
            await manager.DeleteAsync("tenant", "model-connection");
        }
    }
}
