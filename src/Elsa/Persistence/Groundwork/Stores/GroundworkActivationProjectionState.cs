namespace Elsa.Persistence.Groundwork.Stores;

internal sealed record GroundworkActivationProjectionState(
    string ProjectionKind,
    string ActivationId,
    bool IsActive);

internal static class GroundworkActivationProjectionTransition
{
    public static bool IsAlreadyActivated(
        GroundworkActivationProjectionState candidate,
        GroundworkActivationProjectionState? replaced,
        bool hasDistinctReplacement)
    {
        if (!candidate.IsActive)
            return false;

        if (!hasDistinctReplacement || replaced is null || !replaced.IsActive)
            return true;

        throw new InvalidOperationException(
            $"Activation projection '{candidate.ActivationId}' is active while its replaced projection '{replaced.ActivationId}' is still active.");
    }

    public static void EnsureCanActivate(
        GroundworkActivationProjectionState candidate,
        GroundworkActivationProjectionState? replaced,
        bool hasDistinctReplacement)
    {
        if (candidate.IsActive)
            throw new InvalidOperationException($"Activation projection '{candidate.ActivationId}' is already active.");

        if (hasDistinctReplacement && replaced?.IsActive != true)
        {
            throw new InvalidOperationException(
                $"Activation projection '{candidate.ActivationId}' cannot replace a projection that is missing or no longer active.");
        }
    }
}
