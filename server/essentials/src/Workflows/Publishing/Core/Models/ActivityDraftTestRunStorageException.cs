namespace Elsa.Workflows.Publishing.Core.Models;

/// <summary>Executable material or source retention failed while preparing an activity draft Test Run.</summary>
/// <remarks>The receipt remains retryable. A write may have committed before acknowledgement failed;
/// callers retry the same idempotency key. The original infrastructure cause is preserved.</remarks>
public sealed class ActivityDraftTestRunStorageException(string testRunId, Exception innerException)
    : InvalidOperationException($"Could not retain the executable source for activity Test Run '{testRunId}'.", innerException)
{
    public string TestRunId { get; } = testRunId;
}
