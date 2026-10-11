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
    /// out of the activator for a lease the handler never got. When both exist, both are kept. Every element of the
    /// aggregate is passed through <paramref name="mask"/>, the handler's masking of its execution's failure text
    /// (<see cref="ActivityFaultMasking.Mask(Exception)"/>), because the aggregate is not a cancellation and is recorded
    /// as a handler fault: each disposal failure, and the cancellation, whose message activity code may have written.
    /// The cancellation element stays a cancellation for the same token (<see cref="MaskedCancellation"/>). With no
    /// disposal failure the arm rethrows the original cancellation, which is not masked.
    /// </summary>
    public static async ValueTask<Exception?> DisposeAfterCancellationAsync(
        ActivityActivationLease? lease,
        OperationCanceledException cancellation,
        string message,
        Func<Exception, Exception> mask)
    {
        var leaseFailure = await TryDisposeAsync(lease);
        var carriedFailure = (cancellation as ActivityActivationCanceledCleanupException)?.DisposalException;
        Exception[] disposalFailures = [.. new[] { leaseFailure, carriedFailure }.OfType<Exception>().Select(mask)];
        return disposalFailures.Length == 0 ? null : new AggregateException(message, [MaskedCancellation(cancellation, mask), .. disposalFailures]);
    }

    /// <summary>
    /// <paramref name="cancellation"/> itself while <paramref name="mask"/> hands it on unchanged (no value is registered
    /// for the execution). Otherwise a new <see cref="OperationCanceledException"/> for the same token, with the masked
    /// message and inner exception chain <paramref name="mask"/> produced.
    /// </summary>
    private static OperationCanceledException MaskedCancellation(OperationCanceledException cancellation, Func<Exception, Exception> mask) =>
        mask(cancellation) is var masked && ReferenceEquals(masked, cancellation)
            ? cancellation
            : new OperationCanceledException(masked.Message, masked.InnerException, cancellation.CancellationToken);

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
