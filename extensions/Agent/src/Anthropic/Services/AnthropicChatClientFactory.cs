using Anthropic;
using Microsoft.Extensions.AI;

namespace Elsa.Agent.Anthropic.Services;

/// <summary>
/// Creates the Microsoft.Extensions.AI <see cref="IChatClient"/> the provider drives. The single SDK touch-point is
/// isolated here so the provider stays SDK-agnostic and tests can substitute a scripted client.
/// </summary>
public interface IAnthropicChatClientFactory
{
    IChatClient Create(string apiKey, string model);
}

/// <summary>Default factory backed by the official <c>Anthropic</c> SDK's <c>AsIChatClient</c> binding.</summary>
public sealed class AnthropicChatClientFactory : IAnthropicChatClientFactory
{
    public IChatClient Create(string apiKey, string model)
        => new AnthropicClient { ApiKey = apiKey }.AsIChatClient(model);
}
