using Anthropic;
using CShells.Features;
using Elsa.Agent.Specialists.Connections;
using Elsa.Agent.Specialists.Core.Contracts;
using Elsa.Connections.Core.Contracts;
using Elsa.Connections.Core.Models;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Agent.Specialists.Anthropic;

/// <summary>Explicit Anthropic specialist model connections, independent of Weaver provider selection.</summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Agent")]
[ShellFeature(name: "AnthropicSpecialists", DisplayName = "Specialists: Anthropic", Description = "Explicit scoped Anthropic model connections for specialists, with SDK retries disabled.", DependsOn = new object[] { "Connections" })]
public class AnthropicSpecialistsFeature : IShellFeature
{
    /// <inheritdoc />
    public virtual void ConfigureServices(IServiceCollection services)
    {
        services.AddPersistenceCore();
        services.AddScoped<AnthropicSpecialistModelProvider>();
        services.AddScoped<ISpecialistModelClientFactory>(sp => sp.GetRequiredService<AnthropicSpecialistModelProvider>());
        services.AddScoped<ISpecialistModelCapabilitiesSource>(sp => sp.GetRequiredService<AnthropicSpecialistModelProvider>());
        services.AddScoped<IConnectionTypeProvider>(sp => sp.GetRequiredService<AnthropicSpecialistModelProvider>());
    }
}

internal class AnthropicSpecialistModelProvider(IConnectionResolver resolver, IPersistenceAccessContextAccessor access)
    : SpecialistConnectionModelProvider(resolver, access)
{
    public override string ProviderKey => "anthropic";
    protected override string ConnectionTypeName => "specialist-anthropic";
    protected override string ProtocolProfile => "anthropic/12.32.0;messages/2023-06-01;meai/10.10.1;http1.1;api-key;retries0;redirects0";
    protected override IChatClient CreateClient(Uri endpoint, string modelId, SensitiveValue apiKey, HttpClient http) =>
        new AnthropicClient(new global::Anthropic.Core.ClientOptions
        {
            ApiKey = apiKey.Reveal(), AuthToken = null, BaseUrl = endpoint.AbsoluteUri.TrimEnd('/'), HttpClient = http, MaxRetries = 0
        }).AsIChatClient(modelId);
}
