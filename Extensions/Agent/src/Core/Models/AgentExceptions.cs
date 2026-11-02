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

/// <summary>
/// Preview: the owning session store refused an insertion because the session policy revision moved after
/// the delivery was authorized. Nothing was inserted; the caller may re-authorize under the current policy.
/// </summary>
public sealed class AgentSessionPolicyChangedException(string sessionId, long expectedRevision, long currentRevision)
    : InvalidOperationException("The session policy changed after this delivery was authorized.")
{
    public const string Code = "agent.message.policy_changed";
    public string SessionId { get; } = sessionId;
    public long ExpectedRevision { get; } = expectedRevision;
    public long CurrentRevision { get; } = currentRevision;
}
