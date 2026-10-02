using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Activities.Runtime.Services;

/// <summary>
/// Activation failed after the activity was created, and disposing its lease failed as well. Carries both failures,
/// and keeps the classification of the activation failure, so a retryable secret resolution failure is still recorded
/// as retryable with its own code when cleanup also fails.
/// </summary>
internal sealed class ActivityActivationCleanupException(Exception activationException, Exception disposalException)
    : AggregateException("Activity input hydration and activation cleanup both failed.", activationException, disposalException),
        IRuntimeFaultClassification
{
    private readonly IRuntimeFaultClassification? _classification = activationException as IRuntimeFaultClassification;

    public bool IsRetryable => _classification?.IsRetryable ?? false;

    public string? FailureCode => _classification?.FailureCode;
}
