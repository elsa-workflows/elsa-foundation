using OpenAI;
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

namespace Elsa.Agent.Specialists.OpenAI;

/// <summary>Explicit OpenAI specialist models, independent of Weaver provider selection.</summary>
[ManifestRuntimeKind(ElsaRuntimeKinds.Server)]
[ManifestFeatureCategory("Agent")]
[ShellFeature(name: "OpenAISpecialists", DisplayName = "Specialists: OpenAI", Description = "Explicit scoped OpenAI models for specialists, with SDK retries disabled.", DependsOn = new object[] { "Connections" })]
public class OpenAISpecialistsFeature : IShellFeature
{
    /// <inheritdoc />
    public virtual void ConfigureServices(IServiceCollection services)
    {
        services.AddPersistenceCore();
        services.AddScoped<OpenAISpecialistModelProvider>();
        services.AddScoped<ISpecialistModelClientFactory>(sp => sp.GetRequiredService<OpenAISpecialistModelProvider>());
        services.AddScoped<ISpecialistModelCapabilitiesSource>(sp => sp.GetRequiredService<OpenAISpecialistModelProvider>());
        services.AddSingleton<IConnectionTypeProvider>(new SpecialistModelConnectionTypeProvider("openai", "specialist-openai"));
    }
}

internal sealed class OpenAISpecialistModelProvider(IConnectionResolver resolver, IPersistenceAccessContextAccessor access)
    : SpecialistConnectionModelProvider(resolver, access)
{
    public override string ProviderKey => "openai";
    protected override string ConnectionTypeName => "specialist-openai";
    protected override bool SupportsPhysicalRequestReservations => true;
    protected override bool SupportsStreaming => true;
    protected override string ProtocolProfile => "openai/2.14.0;chat-completions/v1;meai/10.10.1;http1.1;bearer;retries0;redirects0;stream1";
    protected override IChatClient CreateClient(Uri endpoint, string modelId, SensitiveValue apiKey, HttpClient http) =>
        new TerminalClient(new OpenAIClient(new ApiKeyCredential(apiKey.Reveal()), new OpenAIClientOptions
        {
            Endpoint = new Uri(endpoint, "/v1"),
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
