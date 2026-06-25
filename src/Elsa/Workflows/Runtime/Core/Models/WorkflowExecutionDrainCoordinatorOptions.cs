namespace Elsa.Workflows.Runtime.Core.Models;

public sealed class WorkflowExecutionDrainCoordinatorOptions
{
    public const int DefaultMaxDrainCycles = 64;
    public const int DefaultOutboxDeliveryBatchSize = 64;

    public WorkflowExecutionDrainCoordinatorOptions(
        int maxDrainCycles = DefaultMaxDrainCycles,
        int outboxDeliveryBatchSize = DefaultOutboxDeliveryBatchSize)
    {
        if (maxDrainCycles <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxDrainCycles), "Maximum drain cycles must be greater than zero.");

        if (outboxDeliveryBatchSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(outboxDeliveryBatchSize), "Outbox delivery batch size must be greater than zero.");

        MaxDrainCycles = maxDrainCycles;
        OutboxDeliveryBatchSize = outboxDeliveryBatchSize;
    }

    public int MaxDrainCycles { get; }
    public int OutboxDeliveryBatchSize { get; }
}
