using System.Security.Claims;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;

namespace Elsa.Agent.Api.Endpoints;

/// <summary>
/// Shared session-access authorization for every session-scoped endpoint (stream, post message, cancel
/// turn, get session, feedback). Resolves the session and confirms the current principal may access it,
/// returning the session together with an <see cref="AgentError"/> to send when access is denied —
/// <c>null</c> error means authorized. Mirrors <see cref="AgentProposalAuthorization"/> so a fix to the
/// access rule lands in exactly one place (issue #414).
/// </summary>
/// <remarks>
/// Authorization runs on the session's plain owner columns first. Only an authorized owner's request reads the session
/// itself, which can hydrate it, take its ownership and settle its recorded work; a foreign or missing caller causes
/// none of those, and never learns of an ownership or unavailable-state outcome.
/// </remarks>
internal static class AgentSessionAuthorization
{
    public static async Task<(AgentSession? Session, AgentError? Error)> AuthorizeAsync(
        IAgentSessionService sessions,
        ClaimsPrincipal user,
        string sessionId,
        CancellationToken ct)
    {
        var owner = await sessions.FindOwnerAsync(sessionId, ct);
        if (owner is null)
            return (null, NotFound(sessionId));
        if (!AgentEndpointActor.CanAccess(owner.ActorId, owner.TenantId, user))
            return (null, new("agent.session.forbidden", "The agent session is not available to the current principal.", 403));

        var session = await sessions.FindAsync(sessionId, ct);
        return session is null ? (null, NotFound(sessionId)) : (session, null);
    }

    private static AgentError NotFound(string sessionId) => new("agent.session.not_found", $"Agent session '{sessionId}' was not found.", 404);
}
