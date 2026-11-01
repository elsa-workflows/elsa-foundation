namespace Elsa.Agent.Specialists.Core.Models;

internal static class SpecialistTurnLeaseRules
{
    // Initial bounded lease profile. It never extends the operation's original absolute deadline.
    internal static readonly TimeSpan Duration = TimeSpan.FromSeconds(30);
    internal static DateTimeOffset Expires(SpecialistTurnDocument document, DateTimeOffset now) =>
        document.Deadline - now <= Duration ? document.Deadline : now.Add(Duration);
    internal static SpecialistTurnLeaseToken Token(SpecialistTurnDocument document) => new(document.Fence!, document.AdmissionGeneration);
    internal static bool Matches(SpecialistTurnDocument document, SpecialistTurnLeaseToken lease) => document.Fence == lease.Fence && document.AdmissionGeneration == lease.Generation;
    internal static bool Live(SpecialistTurnDocument document, SpecialistTurnLeaseToken lease, DateTimeOffset now) =>
        Matches(document, lease) && document.AdmissionGeneration > 0 && document.LeaseExpiresAt > now && document.Deadline > now;
    internal static bool CanClaim(SpecialistTurnDocument document, string childInvocationId, DateTimeOffset now) =>
        document.Status == SpecialistTurnStatus.Prepared || document.Status == SpecialistTurnStatus.Claimed &&
        document.ChildInvocationId == childInvocationId && (document.LeaseExpiresAt is null || document.LeaseExpiresAt <= now) &&
        document.ModelRequests is not { Count: > 0 } && document.ResponseObservation is null;
    internal static SpecialistTurnDocument Claim(SpecialistTurnDocument document, string childInvocationId, string fence, DateTimeOffset now) =>
        document with { Status = SpecialistTurnStatus.Claimed, ChildInvocationId = childInvocationId, Fence = fence,
            AdmissionGeneration = checked(document.AdmissionGeneration + 1), LeaseExpiresAt = Expires(document, now) };
}
