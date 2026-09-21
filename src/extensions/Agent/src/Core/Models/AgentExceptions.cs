namespace Elsa.Agent.Core.Models;

/// <summary>
/// Thrown when more than one agent harness/provider is registered in a composition. Exactly one
/// <see cref="Contracts.IAgentProvider"/> may be enabled at a time.
/// </summary>
public sealed class AgentHarnessConflictException(IReadOnlyCollection<string> providerIds)
    : InvalidOperationException($"More than one agent harness is enabled: {string.Join(", ", providerIds)}. Enable exactly one.")
{
    public IReadOnlyCollection<string> ProviderIds { get; } = providerIds;
}
