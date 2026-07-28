namespace Elsa.Persistence.Groundwork.Stores;

internal sealed record GroundworkPublicationProjectionState(
    string ProjectionKind,
    string PublicationId,
    bool IsActive);

internal static class GroundworkPublicationProjectionTransition
{
    public static bool IsAlreadyActivated(
        GroundworkPublicationProjectionState candidate,
        GroundworkPublicationProjectionState? replaced,
        bool hasDistinctReplacement)
    {
        if (!candidate.IsActive)
            return false;

        if (!hasDistinctReplacement || replaced is null || !replaced.IsActive)
            return true;

        throw new InvalidOperationException(
            $"Publication projection '{candidate.PublicationId}' is active while its replaced projection '{replaced.PublicationId}' is still active.");
    }

    public static void EnsureCanActivate(
        GroundworkPublicationProjectionState candidate,
        GroundworkPublicationProjectionState? replaced,
        bool hasDistinctReplacement)
    {
        if (candidate.IsActive)
            throw new InvalidOperationException($"Publication projection '{candidate.PublicationId}' is already active.");

        if (hasDistinctReplacement && replaced?.IsActive != true)
        {
            throw new InvalidOperationException(
                $"Publication projection '{candidate.PublicationId}' cannot replace a projection that is missing or no longer active.");
        }
    }
}
