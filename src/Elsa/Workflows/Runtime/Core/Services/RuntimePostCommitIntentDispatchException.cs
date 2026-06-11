namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class RuntimePostCommitIntentDispatchException : Exception
{
    public RuntimePostCommitIntentDispatchException(
        string commitId,
        string failedIntentId,
        IReadOnlyCollection<string> dispatchedIntentIds,
        IReadOnlyCollection<string> undispatchedIntentIds,
        Exception innerException)
        : this(commitId, failedIntentId, dispatchedIntentIds, undispatchedIntentIds, innerException, null)
    {
    }

    public RuntimePostCommitIntentDispatchException(
        string commitId,
        string failedIntentId,
        IReadOnlyCollection<string> dispatchedIntentIds,
        IReadOnlyCollection<string> undispatchedIntentIds,
        Exception innerException,
        Exception? deliveryResultRecordingException)
        : base($"Post-commit intent '{failedIntentId}' failed after checkpoint commit '{commitId}' was written.", innerException)
    {
        CommitId = commitId;
        FailedIntentId = failedIntentId;
        DispatchedIntentIds = dispatchedIntentIds;
        UndispatchedIntentIds = undispatchedIntentIds;
        DeliveryResultRecordingException = deliveryResultRecordingException;
    }

    public string CommitId { get; }
    public string FailedIntentId { get; }
    public IReadOnlyCollection<string> DispatchedIntentIds { get; }
    public IReadOnlyCollection<string> UndispatchedIntentIds { get; }
    public Exception? DeliveryResultRecordingException { get; }
}
