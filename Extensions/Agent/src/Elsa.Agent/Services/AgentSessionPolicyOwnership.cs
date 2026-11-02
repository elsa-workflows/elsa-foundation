using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Agent.Services;

/// <summary>The kinds of admitted work that own a session's current policy.</summary>
internal enum AgentSessionClaimKind
{
    Prompt,
    Foreground,
    Preparation,
    Proposal,
    Pause
}

/// <summary>
/// An opaque, server-only ownership token on one session row. Identity is by reference: releasing an
/// old token never releases a successor. Never accepted from or emitted to transport or module payloads.
/// </summary>
internal sealed class AgentSessionClaim
{
    internal AgentSessionClaim(string sessionId, AgentSessionClaimKind kind, AgentSessionPolicyState state, DateTimeOffset? deadline)
    {
        SessionId = sessionId;
        Kind = kind;
        State = state;
        Deadline = deadline;
    }

    public string SessionId { get; }
    public AgentSessionClaimKind Kind { get; }

    /// <summary>The policy snapshot and revision that govern the claimed work; read only after acquisition.</summary>
    public AgentSessionPolicyState State { get; }

    // Mutated only under the owning row monitor.
    internal string? Key { get; set; }
    internal DateTimeOffset? Deadline { get; }
    internal bool Dispatched { get; set; }
}

/// <summary>
/// A default lifecycle service that acquires its claims from <see cref="PolicyOwner"/>. The owner is the concrete
/// default session service: it owns the session rows, so it is never replaced independently of them.
/// </summary>
internal interface IAgentSessionPolicyParticipant
{
    /// <summary>The owner this instance and its claim-holding collaborators participate with, or null.</summary>
    InMemoryAgentSessionService? PolicyOwner { get; }
}

internal static class AgentSessionPolicyParticipation
{
    internal static InMemoryAgentSessionService? SameOwner(InMemoryAgentSessionService? owner, params object[] participants)
        => owner is not null && participants.All(participant => participant is IAgentSessionPolicyParticipant candidate
            && ReferenceEquals(candidate.PolicyOwner, owner)) ? owner : null;
}

/// <summary>
/// Preview policy readback and conditional autonomy update. Supported only when the resolved session
/// service, orchestrator, invoker and proposal service are default participants of one owner instance.
/// </summary>
internal sealed class DefaultAgentSessionPolicyService(IServiceProvider services) : IAgentSessionPolicyService
{
    private static readonly AgentSessionPolicyResult Unsupported = new(AgentSessionPolicyOutcome.Unsupported, null);

    public Task<AgentSessionPolicyResult> GetAsync(string sessionId, string actorId, string tenantId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Owner() is { } owner ? owner.GetPolicy(sessionId, actorId, tenantId) : Unsupported);
    }

    public Task<AgentSessionPolicyResult> ChangeAutonomyAsync(AgentSessionAutonomyChangeRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(Owner() is { } owner ? owner.ChangeAutonomy(request) : Unsupported);
    }

    // Participants are resolved lazily so that an unrelated provider conflict surfaces from its own consumer.
    private InMemoryAgentSessionService? Owner()
    {
        var sessions = services.GetRequiredService<IAgentSessionService>();
        var owner = sessions as InMemoryAgentSessionService;
        return AgentSessionPolicyParticipation.SameOwner(owner, sessions,
            services.GetRequiredService<IAgentStreamingService>(),
            services.GetRequiredService<IAgentToolInvoker>(),
            services.GetRequiredService<IAgentProposalService>());
    }
}
