namespace Elsa.Weaver.Core.Models;

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
    public const string Code = "weaver.message.policy_changed";
    public string SessionId { get; } = sessionId;
    public long ExpectedRevision { get; } = expectedRevision;
    public long CurrentRevision { get; } = currentRevision;
}

/// <summary>
/// Preview: another process owns the session's active work (<see cref="OwnedElsewhere"/>, retryable after
/// <see cref="RetryAt"/>), or this process lost that ownership while acting (<see cref="OwnershipLost"/>). Nothing
/// was applied by the refused operation.
/// </summary>
public sealed class AgentSessionOwnershipException(string sessionId, string code, string message, DateTimeOffset? retryAt = null)
    : InvalidOperationException(message)
{
    public const string OwnedElsewhere = "weaver.session.owned_elsewhere";
    public const string OwnershipLost = "weaver.session.ownership_lost";

    public string SessionId { get; } = sessionId;
    public string Code { get; } = code;
    /// <summary>When the other owner's lease lapses, for a retry hint; null when unknown.</summary>
    public DateTimeOffset? RetryAt { get; } = retryAt;

    /// <summary>The ownership refusal anywhere in a causal chain, including wrapped and aggregated causes.</summary>
    public static AgentSessionOwnershipException? Find(Exception? exception) => exception switch
    {
        null => null,
        AgentSessionOwnershipException ownership => ownership,
        AggregateException aggregate => aggregate.InnerExceptions.Select(Find).FirstOrDefault(x => x is not null),
        _ => Find(exception.InnerException)
    };
}

/// <summary>
/// Preview: a session's recorded state is unreadable, corrupt or incompatible (FR-018), for example a recorded disclosure
/// snapshot whose text no longer matches its digest. Nothing was hydrated, settled or changed; the session is
/// unavailable until its record is repaired. <see cref="Reason"/> names what failed verification.
/// </summary>
public sealed class AgentSessionUnavailableException : InvalidOperationException
{
    public const string Code = "weaver.session.unavailable_state";

    public AgentSessionUnavailableException(string sessionId, string reason) : this(sessionId, reason, null)
    {
    }

    /// <param name="innerException">The store or decoding failure that made the recorded state unavailable, if any.</param>
    public AgentSessionUnavailableException(string sessionId, string reason, Exception? innerException)
        : base($"The recorded state of agent session '{sessionId}' is unavailable ({reason}); nothing was changed.", innerException)
    {
        SessionId = sessionId;
        Reason = reason;
    }

    public string SessionId { get; }
    public string Reason { get; }

    /// <summary>The unavailable-state refusal anywhere in a causal chain, including wrapped and aggregated causes.</summary>
    public static AgentSessionUnavailableException? Find(Exception? exception) => exception switch
    {
        null => null,
        AgentSessionUnavailableException unavailable => unavailable,
        AggregateException aggregate => aggregate.InnerExceptions.Select(Find).FirstOrDefault(x => x is not null),
        _ => Find(exception.InnerException)
    };
}
