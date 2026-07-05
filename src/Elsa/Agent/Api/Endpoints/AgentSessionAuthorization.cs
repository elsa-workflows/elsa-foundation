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
internal static class AgentSessionAuthorization
{
    public static async Task<(AgentSession? Session, AgentError? Error)> AuthorizeAsync(
        IAgentSessionService sessions,
        ClaimsPrincipal user,
        string sessionId,
        CancellationToken ct)
    {
        var session = await sessions.FindAsync(sessionId, ct);
        if (session is null)
            return (null, new("agent.session.not_found", $"Agent session '{sessionId}' was not found.", 404));

        return AgentEndpointActor.CanAccess(session.ActorId, session.TenantId, user)
            ? (session, null)
            : (session, new("agent.session.forbidden", "The agent session is not available to the current principal.", 403));
    }
}
