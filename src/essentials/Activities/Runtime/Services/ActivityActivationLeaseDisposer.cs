using Elsa.Activities.Runtime.Contracts;

namespace Elsa.Activities.Runtime.Services;

/// <summary>Closes an activation lease without letting cleanup failure escape the runtime fault boundary.</summary>
internal static class ActivityActivationLeaseDisposer
{
    public static async ValueTask<Exception?> TryDisposeAsync(ActivityActivationLease? lease)
    {
        if (lease is null)
            return null;

        try
        {
            await lease.DisposeAsync();
            return null;
        }
        catch (Exception exception)
        {
            return exception;
        }
    }

    /// <summary>
    /// Disposes <paramref name="lease"/> after <paramref name="cancellation"/>, and returns the disposal failure to
    /// report: its own, or the one a canceled activation carried out of the activator, whose lease the caller never got.
    /// </summary>
    public static async ValueTask<Exception?> TryDisposeAfterCancellationAsync(ActivityActivationLease? lease, OperationCanceledException cancellation) =>
        await TryDisposeAsync(lease) ?? (cancellation as ActivityActivationCanceledCleanupException)?.DisposalException;

    /// <summary>
    /// Combines a failure while the activity was activated, before it ran, with the failure of disposing its lease,
    /// keeping the classification of <paramref name="primary"/>. When <paramref name="primary"/> is the cancellation
    /// of the activation, the result is still a cancellation, carrying <paramref name="disposal"/>.
    /// </summary>
    public static Exception CombineActivationFailure(Exception primary, Exception disposal, CancellationToken cancellationToken) =>
        primary is OperationCanceledException cancellation && cancellationToken.IsCancellationRequested
            ? new ActivityActivationCanceledCleanupException(cancellation, disposal)
            : new ActivityActivationCleanupException("Activity activation and activation disposal both failed.", primary, disposal);

    /// <summary>Combines a failure of the activity's work with the failure of disposing its lease, keeping the classification of <paramref name="primary"/>.</summary>
    public static Exception CombineExecutionFailure(Exception primary, Exception disposal) =>
        new ActivityActivationCleanupException("Activity execution and activation disposal both failed.", primary, disposal);
}
