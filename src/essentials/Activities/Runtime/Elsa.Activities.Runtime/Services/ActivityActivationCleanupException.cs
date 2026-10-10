using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Activities.Runtime.Services;

/// <summary>
/// An activity's activation or its execution failed, and disposing its activation lease failed as well. Carries both
/// failures, and keeps the classification of the primary failure, so a retryable secret resolution failure is still
/// recorded as retryable with its own code when cleanup also fails. The message names the phase that failed. Created
/// by <see cref="ActivityActivationLeaseDisposer.CombineActivationFailure"/> and
/// <see cref="ActivityActivationLeaseDisposer.CombineExecutionFailure"/>.
/// </summary>
internal sealed class ActivityActivationCleanupException(string message, Exception primaryException, Exception disposalException)
    : AggregateException(message, primaryException, disposalException),
        IRuntimeFaultClassification
{
    private readonly IRuntimeFaultClassification? _classification = primaryException as IRuntimeFaultClassification;

    public bool IsRetryable => _classification?.IsRetryable ?? false;

    public string? FailureCode => _classification?.FailureCode;
}
