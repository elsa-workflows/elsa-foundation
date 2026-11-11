using System.Security.Claims;
using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;

namespace Elsa.Weaver.Api.Endpoints;

/// <summary>
/// Shared session-access authorization for every session-scoped endpoint (stream, post message, cancel
/// turn, get session, feedback). Resolves the session and confirms the current principal may access it,
/// returning the session together with an <see cref="WeaverError"/> to send when access is denied —
/// <c>null</c> error means authorized. Mirrors <see cref="WeaverProposalAuthorization"/> so a fix to the
/// access rule lands in exactly one place (issue #414).
/// </summary>
/// <remarks>
/// Authorization runs on the session's plain owner columns first. Only an authorized owner's request reads the session
/// itself, which can hydrate it, take its ownership and settle its recorded work; a foreign or missing caller causes
/// none of those, and never learns of an ownership or unavailable-state outcome.
/// </remarks>
internal static class WeaverSessionAuthorization
{
    public static async Task<(WeaverSession? Session, WeaverError? Error)> AuthorizeAsync(
        IWeaverSessionService sessions,
        ClaimsPrincipal user,
        string sessionId,
        CancellationToken ct)
    {
        var owner = await sessions.FindOwnerAsync(sessionId, ct);
        if (owner is null)
            return (null, NotFound(sessionId));
        if (!WeaverEndpointActor.CanAccess(owner.ActorId, owner.TenantId, user))
            return (null, new("weaver.session.forbidden", "The agent session is not available to the current principal.", 403));

        var session = await sessions.FindAsync(sessionId, ct);
        return session is null ? (null, NotFound(sessionId)) : (session, null);
    }

    private static WeaverError NotFound(string sessionId) => new("weaver.session.not_found", $"Agent session '{sessionId}' was not found.", 404);
}
