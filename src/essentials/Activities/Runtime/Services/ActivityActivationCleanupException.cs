using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Activities.Runtime.Services;

/// <summary>
/// An activity's work failed, and disposing its activation lease failed as well. Carries both failures, and keeps the
/// classification of the work's failure, so a retryable secret resolution failure is still recorded as retryable with
/// its own code when cleanup also fails. Created by <see cref="ActivityActivationLeaseDisposer.Combine"/>.
/// </summary>
internal sealed class ActivityActivationCleanupException(Exception primaryException, Exception disposalException)
    : AggregateException("Activity execution and activation disposal both failed.", primaryException, disposalException),
        IRuntimeFaultClassification
{
    private readonly IRuntimeFaultClassification? _classification = primaryException as IRuntimeFaultClassification;

    public bool IsRetryable => _classification?.IsRetryable ?? false;

    public string? FailureCode => _classification?.FailureCode;
}
