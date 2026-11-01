using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Agent.Services;

/// <summary>The kinds of admitted work that own a session's current policy (#3440, experimental).</summary>
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
/// The session row owner of policy, policy revision and claims. Every member is a short transition under
/// the per-session monitor; none awaits, performs I/O or invokes callbacks.
/// </summary>
internal interface IAgentSessionPolicyOwner
{
    /// <summary>Acquires an unkeyed claim; null when this owner has no row for the session.</summary>
    AgentSessionClaim? Acquire(string sessionId, AgentSessionClaimKind kind);

    /// <summary>Acquires a provisional proposal claim before store insertion; it records the prepared deadline.</summary>
    AgentSessionClaim? AcquireProposal(string sessionId, DateTimeOffset? deadline);

    /// <summary>Binds a provisional proposal claim to its inserted proposal identity.</summary>
    bool BindProposal(AgentSessionClaim claim, string proposalId);

    /// <summary>Marks the proposal's claim dispatched; false refuses a pre-dispatch claim past its deadline.</summary>
    bool MarkProposalDispatched(string sessionId, string proposalId);

    /// <summary>Releases the claim bound to this proposal identity, if any.</summary>
    void ReleaseProposal(string sessionId, string proposalId);

    /// <summary>Returns the retained pause claim, acquiring it when absent.</summary>
    AgentSessionClaim? EnsurePause(string sessionId);

    /// <summary>The currently retained pause claim, if any.</summary>
    AgentSessionClaim? FindPause(string sessionId);

    /// <summary>Releases exactly this token. A released or superseded token is a no-op.</summary>
    void Release(AgentSessionClaim claim);

    AgentSessionPolicyResult GetPolicy(string sessionId, string actorId, string tenantId);

    AgentSessionPolicyResult ChangeAutonomy(AgentSessionAutonomyChangeRequest request);
}

/// <summary>A default lifecycle service that acquires its claims from <see cref="PolicyOwner"/>.</summary>
internal interface IAgentSessionPolicyParticipant
{
    /// <summary>The owner this instance and its claim-holding collaborators participate with, or null.</summary>
    IAgentSessionPolicyOwner? PolicyOwner { get; }
}

internal static class AgentSessionPolicyParticipation
{
    internal static IAgentSessionPolicyOwner? SameOwner(IAgentSessionPolicyOwner? owner, params object[] participants)
        => owner is not null && participants.All(participant => participant is IAgentSessionPolicyParticipant candidate
            && ReferenceEquals(candidate.PolicyOwner, owner)) ? owner : null;
}

/// <summary>
/// Experimental policy readback and conditional autonomy update. Supported only when the resolved session
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
    private IAgentSessionPolicyOwner? Owner()
    {
        var sessions = services.GetRequiredService<IAgentSessionService>();
        var owner = sessions as IAgentSessionPolicyOwner;
        return AgentSessionPolicyParticipation.SameOwner(owner, sessions,
            services.GetRequiredService<IAgentStreamingService>(),
            services.GetRequiredService<IAgentToolInvoker>(),
            services.GetRequiredService<IAgentProposalService>());
    }
}
