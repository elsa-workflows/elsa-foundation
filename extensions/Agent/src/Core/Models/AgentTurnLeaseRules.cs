namespace Elsa.Agent.Core.Models;

internal static class AgentTurnLeaseRules
{
    // Initial bounded lease profile. It never extends the operation's original absolute deadline.
    internal static readonly TimeSpan Duration = TimeSpan.FromSeconds(30);
    internal static DateTimeOffset Expires(AgentTurnDocument document, DateTimeOffset now) =>
        document.Deadline - now <= Duration ? document.Deadline : now.Add(Duration);
    internal static AgentTurnLeaseToken Token(AgentTurnDocument document) => new(document.Fence!, document.AdmissionGeneration);
    internal static bool Matches(AgentTurnDocument document, AgentTurnLeaseToken lease) => document.Fence == lease.Fence && document.AdmissionGeneration == lease.Generation;
    internal static bool Live(AgentTurnDocument document, AgentTurnLeaseToken lease, DateTimeOffset now) =>
        Matches(document, lease) && document.AdmissionGeneration > 0 && document.LeaseExpiresAt > now && document.Deadline > now;
    internal static bool CanClaim(AgentTurnDocument document, AgentInvocationIdentity identity, string childInvocationId, DateTimeOffset now) =>
        document.Status == AgentTurnStatus.Prepared || document.Status == AgentTurnStatus.Claimed &&
        AgentTurnDeliveryRules.CanBindChild(document, identity, childInvocationId) &&
        (document.DeliveryProtocol == 1 || document.ChildInvocationId == childInvocationId) && (document.LeaseExpiresAt is null || document.LeaseExpiresAt <= now) &&
        document.ModelRequests is not { Count: > 0 } && document.ResponseObservation is null;
    internal static AgentTurnDocument Claim(AgentTurnDocument document, AgentInvocationIdentity identity, string childInvocationId, string fence, DateTimeOffset now) =>
        AgentTurnDeliveryRules.BindChild(document, identity, childInvocationId) with { ActiveDeliveryActivityId = document.DeliveryProtocol == 1 ? identity.ActivityExecutionId : null, Status = AgentTurnStatus.Claimed, ChildInvocationId = childInvocationId, Fence = fence,
            AdmissionGeneration = checked(document.AdmissionGeneration + 1), LeaseExpiresAt = Expires(document, now) };
}
