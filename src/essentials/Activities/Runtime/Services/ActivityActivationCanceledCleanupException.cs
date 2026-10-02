namespace Elsa.Activities.Runtime.Services;

/// <summary>
/// An activation was canceled, and disposing its activation lease failed as well. It is a cancellation, so a handler
/// that recognizes the activation's cancellation still does, rather than recording a fault; it carries
/// <see cref="DisposalException"/> so that handler can report the disposal failure the way it reports one from its own
/// lease. Created by <see cref="ActivityActivationLeaseDisposer.CombineActivationFailure"/>.
/// </summary>
internal sealed class ActivityActivationCanceledCleanupException(OperationCanceledException cancellation, Exception disposalException)
    : OperationCanceledException("Activity activation was canceled, and disposing its activation lease failed.", cancellation, cancellation.CancellationToken)
{
    /// <summary>The failure of disposing the lease of the canceled activation.</summary>
    public Exception DisposalException { get; } = disposalException;
}
