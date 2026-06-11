namespace Elsa.Workflows.Runtime.Core.Models;

public sealed class RuntimeGeneratorEmissionScheduleRequest
{
    public RuntimeGeneratorEmissionScheduleRequest(
        GeneratedEvent generatedEvent,
        string reason,
        DateTimeOffset? enqueuedAt = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        ArgumentNullException.ThrowIfNull(generatedEvent);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        GeneratedEvent = generatedEvent;
        Reason = reason;
        EnqueuedAt = enqueuedAt;
        Metadata = RuntimeModelMetadata.Snapshot(metadata);
    }

    public GeneratedEvent GeneratedEvent { get; }
    public string Reason { get; }
    public DateTimeOffset? EnqueuedAt { get; }
    public IReadOnlyDictionary<string, string> Metadata { get; }
}

public sealed class RuntimeGeneratorEmissionScheduleResult
{
    public RuntimeGeneratorEmissionScheduleResult(
        SchedulerGeneratedEventWorkItem generatedEventWorkItem,
        RuntimeSchedulerWorkItem schedulerWorkItem)
    {
        ArgumentNullException.ThrowIfNull(generatedEventWorkItem);
        ArgumentNullException.ThrowIfNull(schedulerWorkItem);

        if (!StringComparer.Ordinal.Equals(generatedEventWorkItem.WorkflowExecutionId, schedulerWorkItem.WorkflowExecutionId))
            throw new ArgumentException("Generated-event work and scheduler work must target the same workflow execution.", nameof(schedulerWorkItem));

        GeneratedEventWorkItem = generatedEventWorkItem;
        SchedulerWorkItem = schedulerWorkItem;
    }

    public SchedulerGeneratedEventWorkItem GeneratedEventWorkItem { get; }
    public RuntimeSchedulerWorkItem SchedulerWorkItem { get; }
}
