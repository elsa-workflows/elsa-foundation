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
    /// The cleanup of a scheduler work handler's cancellation arm: disposes <paramref name="lease"/> after
    /// <paramref name="cancellation"/> and returns what the arm throws instead of rethrowing the cancellation. That is
    /// <see langword="null"/> when no disposal failed, so the arm rethrows the cancellation unchanged. Otherwise it is an
    /// <see cref="AggregateException"/> with <paramref name="message"/> holding the cancellation followed by every
    /// disposal failure: the failure of disposing <paramref name="lease"/>, then the one a canceled activation carried
    /// out of the activator for a lease the handler never got. When both exist, both are kept.
    /// </summary>
    public static async ValueTask<Exception?> DisposeAfterCancellationAsync(ActivityActivationLease? lease, OperationCanceledException cancellation, string message)
    {
        var leaseFailure = await TryDisposeAsync(lease);
        var carriedFailure = (cancellation as ActivityActivationCanceledCleanupException)?.DisposalException;
        Exception[] disposalFailures = [.. new[] { leaseFailure, carriedFailure }.OfType<Exception>()];
        return disposalFailures.Length == 0 ? null : new AggregateException(message, [cancellation, .. disposalFailures]);
    }

    /// <summary>
    /// Combines a failure while the activity was activated, before it ran, with the failure of disposing its lease,
    /// keeping the classification of <paramref name="primary"/>. When <paramref name="primary"/> is the cancellation
    /// of the activation, the result is still a cancellation, carrying <paramref name="disposal"/>.
    /// </summary>
    /// <param name="primary">The activation failure.</param>
    /// <param name="disposal">The failure of disposing the activation's lease.</param>
    /// <param name="canceled">
    /// Whether the activation's token was canceled when <paramref name="primary"/> was caught, read before any await:
    /// a token canceled while the lease was disposed does not make an earlier failure the activation's cancellation.
    /// </param>
    public static Exception CombineActivationFailure(Exception primary, Exception disposal, bool canceled) =>
        canceled && primary is OperationCanceledException cancellation
            ? new ActivityActivationCanceledCleanupException(cancellation, disposal)
            : new ActivityActivationCleanupException("Activity activation and activation disposal both failed.", primary, disposal);

    /// <summary>Combines a failure of the activity's work with the failure of disposing its lease, keeping the classification of <paramref name="primary"/>.</summary>
    public static Exception CombineExecutionFailure(Exception primary, Exception disposal) =>
        new ActivityActivationCleanupException("Activity execution and activation disposal both failed.", primary, disposal);
}
