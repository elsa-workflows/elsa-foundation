using Azure.AI.OpenAI;
using CShells.Features;
using Elsa.Agent.Connections;
using Elsa.Agent.Core.Contracts;
using Elsa.Connections.Core.Contracts;
using Elsa.Connections.Core.Models;
using Elsa.Specifications.PackageManifest.Generator.Hints;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using System.ClientModel;
using System.ClientModel.Primitives;

namespace Elsa.Agent.AzureOpenAI;

/// <summary>Explicit Azure OpenAI agent deployments, independent of Weaver provider selection.</summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Agent")]
[ShellFeature(name: "AzureOpenAIAgents", DisplayName = "Agents: Azure OpenAI", Description = "Explicit scoped Azure OpenAI deployments for agents, with SDK retries disabled.", DependsOn = new object[] { "Connections" })]
public class AzureOpenAIAgentsFeature : IShellFeature
{
    /// <inheritdoc />
    public virtual void ConfigureServices(IServiceCollection services)
    {
        services.AddPersistenceCore();
        services.AddScoped<AzureOpenAIAgentModelProvider>();
        services.AddScoped<IAgentModelClientFactory>(sp => sp.GetRequiredService<AzureOpenAIAgentModelProvider>());
        services.AddScoped<IAgentModelCapabilitiesSource>(sp => sp.GetRequiredService<AzureOpenAIAgentModelProvider>());
        services.AddSingleton<IConnectionTypeProvider>(new AgentModelConnectionTypeProvider("azure-openai", "agent-azure-openai"));
    }
}

internal sealed class AzureOpenAIAgentModelProvider(IConnectionResolver resolver, IPersistenceAccessContextAccessor access)
    : AgentConnectionModelProvider(resolver, access)
{
    public override string ProviderKey => "azure-openai";
    protected override string ConnectionTypeName => "agent-azure-openai";
    protected override bool SupportsPhysicalRequestReservations => true;
    protected override bool SupportsStreaming => true;
    protected override string ProtocolProfile => "azure-openai/2.1.0;api/2024-10-21;openai/2.14.0;meai/10.10.1;http1.1;api-key;retries0;redirects0;stream1";
    protected override IChatClient CreateClient(Uri endpoint, string modelId, SensitiveValue apiKey, HttpClient http) =>
        new TerminalClient(new AzureOpenAIClient(endpoint, new ApiKeyCredential(apiKey.Reveal()), new AzureOpenAIClientOptions(AzureOpenAIClientOptions.ServiceVersion.V2024_10_21)
        {
            Transport = new HttpClientPipelineTransport(http), RetryPolicy = new ClientRetryPolicy(0)
        }).GetChatClient(modelId).AsIChatClient());

    private sealed class TerminalClient(IChatClient inner) : DelegatingChatClient(inner)
    {
        public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await foreach (var update in base.GetStreamingResponseAsync(messages, options, cancellationToken).WithCancellation(cancellationToken))
            {
                // This binding exposes streaming refusal only on the raw SDK update, unlike its
                // atomic ErrorContent mapping. Surface the refusal before stripping provider data.
                if (update.RawRepresentation is global::OpenAI.Chat.StreamingChatCompletionUpdate raw && !string.IsNullOrEmpty(raw.RefusalUpdate))
                    update.FinishReason = ChatFinishReason.ContentFilter;
                yield return update;
            }
        }
    }
}
