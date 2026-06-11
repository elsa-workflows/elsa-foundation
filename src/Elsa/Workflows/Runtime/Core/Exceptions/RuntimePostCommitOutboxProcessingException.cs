namespace Elsa.Workflows.Runtime.Core.Exceptions;

public sealed class RuntimePostCommitOutboxProcessingException : Exception
{
    public RuntimePostCommitOutboxProcessingException(
        string outboxItemId,
        string intentId,
        Exception dispatchException,
        Exception deliveryResultRecordingException)
        : base($"Post-commit outbox item '{outboxItemId}' failed during delivery processing.", dispatchException)
    {
        OutboxItemId = outboxItemId;
        IntentId = intentId;
        DeliveryResultRecordingException = deliveryResultRecordingException;
    }

    public string OutboxItemId { get; }
    public string IntentId { get; }
    public Exception DeliveryResultRecordingException { get; }
}
