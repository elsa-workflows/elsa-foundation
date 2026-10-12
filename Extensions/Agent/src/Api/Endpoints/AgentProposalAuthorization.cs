using System.Security.Claims;
using Elsa.Agent.Core.Contracts;
using Elsa.Agent.Core.Models;

namespace Elsa.Agent.Api.Endpoints;

/// <summary>
/// Shared proposal-access authorization for the proposal decision endpoints (approve, deny, execute).
/// Resolves the proposal and its owning session and confirms the current principal may act on it,
/// returning an <see cref="AgentError"/> to send when access is denied or <c>null</c> when authorized.
/// </summary>
internal static class AgentProposalAuthorization
{
    public static async Task<AgentError?> AuthorizeAsync(
        IAgentProposalService proposals,
        IAgentSessionService sessions,
        ClaimsPrincipal user,
        string proposalId,
        CancellationToken ct)
    {
        var proposal = await proposals.FindAsync(proposalId, ct);
        if (proposal is null)
            return new("agent.proposal.not_found", $"Proposal '{proposalId}' was not found.", 404);

        var session = await sessions.FindAsync(proposal.SessionId, ct);
        if (session is null)
            return new("agent.session.not_found", $"Agent session '{proposal.SessionId}' was not found.", 404);

        return AgentEndpointActor.CanAccess(session.ActorId, session.TenantId, user)
            ? null
            : new("agent.proposal.forbidden", "The agent proposal is not available to the current principal.", 403);
    }
}
