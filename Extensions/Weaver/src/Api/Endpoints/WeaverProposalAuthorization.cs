using System.Security.Claims;
using Elsa.Weaver.Core.Contracts;
using Elsa.Weaver.Core.Models;

namespace Elsa.Weaver.Api.Endpoints;

/// <summary>
/// Shared proposal-access authorization for the proposal decision endpoints (approve, deny, execute).
/// Resolves the proposal and its owning session and confirms the current principal may act on it,
/// returning an <see cref="WeaverError"/> to send when access is denied or <c>null</c> when authorized.
/// </summary>
/// <remarks>
/// The proposal resolves to its session, and the caller is authorized on that session's plain owner columns, before
/// the proposal itself is read; reading it can hydrate the session. A foreign caller causes no hydration, takeover or
/// settlement.
/// </remarks>
internal static class WeaverProposalAuthorization
{
    public static async Task<WeaverError?> AuthorizeAsync(
        IWeaverProposalService proposals,
        IWeaverSessionService sessions,
        ClaimsPrincipal user,
        string proposalId,
        CancellationToken ct)
    {
        var sessionId = await proposals.FindSessionIdAsync(proposalId, ct);
        if (sessionId is null)
            return NotFound(proposalId);

        var owner = await sessions.FindOwnerAsync(sessionId, ct);
        if (owner is null)
            return new("weaver.session.not_found", $"Agent session '{sessionId}' was not found.", 404);
        if (!WeaverEndpointActor.CanAccess(owner.ActorId, owner.TenantId, user))
            return new("weaver.proposal.forbidden", "The agent proposal is not available to the current principal.", 403);

        return await proposals.FindAsync(proposalId, ct) is null ? NotFound(proposalId) : null;
    }

    private static WeaverError NotFound(string proposalId) => new("weaver.proposal.not_found", $"Proposal '{proposalId}' was not found.", 404);
}
