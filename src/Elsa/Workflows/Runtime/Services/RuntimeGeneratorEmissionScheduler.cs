using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class RuntimeGeneratorEmissionScheduler : IRuntimeGeneratorEmissionScheduler
{
    private readonly IWorkflowSchedulerWorkQueue _schedulerWorkQueue;
    private readonly TimeProvider _timeProvider;
    private readonly JsonSerializerOptions _payloadJsonSerializerOptions;

    public RuntimeGeneratorEmissionScheduler(IWorkflowSchedulerWorkQueue schedulerWorkQueue)
        : this(schedulerWorkQueue, TimeProvider.System, CreateDefaultPayloadJsonSerializerOptions())
    {
    }

    public RuntimeGeneratorEmissionScheduler(
        IWorkflowSchedulerWorkQueue schedulerWorkQueue,
        TimeProvider timeProvider)
        : this(schedulerWorkQueue, timeProvider, CreateDefaultPayloadJsonSerializerOptions())
    {
    }

    public RuntimeGeneratorEmissionScheduler(
        IWorkflowSchedulerWorkQueue schedulerWorkQueue,
        TimeProvider timeProvider,
        JsonSerializerOptions payloadJsonSerializerOptions)
    {
        ArgumentNullException.ThrowIfNull(schedulerWorkQueue);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(payloadJsonSerializerOptions);

        _schedulerWorkQueue = schedulerWorkQueue;
        _timeProvider = timeProvider;
        _payloadJsonSerializerOptions = new JsonSerializerOptions(payloadJsonSerializerOptions);
    }

    public async ValueTask<RuntimeGeneratorEmissionScheduleResult> ScheduleAsync(
        RuntimeGeneratorEmissionScheduleRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var generatedEvent = request.GeneratedEvent;
        var generatedEventWorkItemId = CreateScopedId(generatedEvent, "generated-event");
        var enqueuedAt = request.EnqueuedAt ?? _timeProvider.GetUtcNow();
        var generatedEventWorkItem = new SchedulerGeneratedEventWorkItem(
            workItemId: generatedEventWorkItemId,
            generatedEvent: generatedEvent,
            enqueuedAt: enqueuedAt,
            reason: request.Reason,
            metadata: request.Metadata);
        var schedulerWorkItem = new RuntimeSchedulerWorkItem(
            workItemId: generatedEventWorkItem.WorkItemId,
            workflowExecutionId: generatedEvent.WorkflowExecutionId,
            commandId: CreateScopedId(generatedEvent, "generated-event-command"),
            commandKind: WorkflowExecutionCommandKind.GeneratedEvent,
            envelopeId: CreateScopedId(generatedEvent, "generated-event-envelope"),
            idempotencyKey: CreateScopedId(generatedEvent, "generated-event-idempotency"),
            enqueuedAt: enqueuedAt,
            recordedAt: _timeProvider.GetUtcNow(),
            sequence: generatedEvent.Sequence,
            payload: JsonSerializer.SerializeToElement(generatedEventWorkItem, _payloadJsonSerializerOptions),
            commandMetadata: CreateCommandMetadata(generatedEvent),
            envelopeMetadata: request.Metadata);

        schedulerWorkItem = await _schedulerWorkQueue.EnqueueAsync(schedulerWorkItem, cancellationToken);
        generatedEventWorkItem = ReadQueuedGeneratedEventWorkItem(schedulerWorkItem, _payloadJsonSerializerOptions);

        return new RuntimeGeneratorEmissionScheduleResult(generatedEventWorkItem, schedulerWorkItem);
    }

    private static string CreateScopedId(GeneratedEvent generatedEvent, string purpose) =>
        $"{generatedEvent.WorkflowExecutionId}:{purpose}:{generatedEvent.GeneratedEventId}";

    private static JsonSerializerOptions CreateDefaultPayloadJsonSerializerOptions() =>
        new(JsonSerializerDefaults.General)
        {
            PropertyNameCaseInsensitive = true
        };

    private static IReadOnlyDictionary<string, string> CreateCommandMetadata(GeneratedEvent generatedEvent)
    {
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["GeneratedEventId"] = generatedEvent.GeneratedEventId,
            ["GeneratorActivityExecutionId"] = generatedEvent.GeneratorActivityExecutionId,
            ["GeneratedEventName"] = generatedEvent.Name,
            ["GeneratedEventDurability"] = generatedEvent.Durability.ToString()
        };

        if (generatedEvent.BranchId is not null)
            metadata["BranchId"] = generatedEvent.BranchId;

        if (generatedEvent.PayloadValue is not null)
            metadata["PayloadValueId"] = generatedEvent.PayloadValue.ValueId;

        return metadata;
    }

    private static SchedulerGeneratedEventWorkItem ReadQueuedGeneratedEventWorkItem(
        RuntimeSchedulerWorkItem schedulerWorkItem,
        JsonSerializerOptions payloadJsonSerializerOptions)
    {
        if (schedulerWorkItem.CommandKind != WorkflowExecutionCommandKind.GeneratedEvent)
            throw new InvalidOperationException($"Queued scheduler work item '{schedulerWorkItem.WorkItemId}' is not generated-event work.");

        if (schedulerWorkItem.Payload is not { } payload)
            throw new InvalidOperationException($"Queued generated-event scheduler work item '{schedulerWorkItem.WorkItemId}' does not carry a generated-event payload.");

        try
        {
            return payload.Deserialize<SchedulerGeneratedEventWorkItem>(payloadJsonSerializerOptions)
                   ?? throw new InvalidOperationException($"Queued generated-event scheduler work item '{schedulerWorkItem.WorkItemId}' payload resolved to null.");
        }
        catch (Exception exception) when (
            exception is JsonException or NotSupportedException or ArgumentException)
        {
            throw new InvalidOperationException($"Queued generated-event scheduler work item '{schedulerWorkItem.WorkItemId}' payload is not valid generated-event work.", exception);
        }
    }
}
