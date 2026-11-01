using Azure.AI.OpenAI;
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
using System.ClientModel;
using System.ClientModel.Primitives;

namespace Elsa.Agent.Specialists.AzureOpenAI;

/// <summary>Explicit Azure OpenAI specialist deployments, independent of Weaver provider selection.</summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Agent")]
[ShellFeature(name: "AzureOpenAISpecialists", DisplayName = "Specialists: Azure OpenAI", Description = "Explicit scoped Azure OpenAI deployments for specialists, with SDK retries disabled.", DependsOn = new object[] { "Connections" })]
public class AzureOpenAISpecialistsFeature : IShellFeature
{
    /// <inheritdoc />
    public virtual void ConfigureServices(IServiceCollection services)
    {
        services.AddPersistenceCore();
        services.AddScoped<AzureOpenAISpecialistModelProvider>();
        services.AddScoped<ISpecialistModelClientFactory>(sp => sp.GetRequiredService<AzureOpenAISpecialistModelProvider>());
        services.AddScoped<ISpecialistModelCapabilitiesSource>(sp => sp.GetRequiredService<AzureOpenAISpecialistModelProvider>());
        services.AddSingleton<IConnectionTypeProvider>(new SpecialistModelConnectionTypeProvider("azure-openai", "specialist-azure-openai"));
    }
}

internal sealed class AzureOpenAISpecialistModelProvider(IConnectionResolver resolver, IPersistenceAccessContextAccessor access)
    : SpecialistConnectionModelProvider(resolver, access)
{
    public override string ProviderKey => "azure-openai";
    protected override string ConnectionTypeName => "specialist-azure-openai";
    protected override bool SupportsPhysicalRequestReservations => true;
    protected override string ProtocolProfile => "azure-openai/2.1.0;api/2024-10-21;openai/2.14.0;meai/10.10.1;http1.1;api-key;retries0;redirects0";
    protected override IChatClient CreateClient(Uri endpoint, string modelId, SensitiveValue apiKey, HttpClient http) =>
        new AzureOpenAIClient(endpoint, new ApiKeyCredential(apiKey.Reveal()), new AzureOpenAIClientOptions(AzureOpenAIClientOptions.ServiceVersion.V2024_10_21)
        {
            Transport = new HttpClientPipelineTransport(http), RetryPolicy = new ClientRetryPolicy(0)
        }).GetChatClient(modelId).AsIChatClient();
}
