using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

public sealed class WorkflowCheckpointSchedulerWorkHandler : IWorkflowSchedulerWorkHandler
{
    public const string HandlerName = nameof(WorkflowCheckpointSchedulerWorkHandler);

    public string Name => HandlerName;

    public bool CanHandle(RuntimeSchedulerWorkItem workItem)
    {
        ArgumentNullException.ThrowIfNull(workItem);

        return workItem.CommandKind == WorkflowExecutionCommandKind.Checkpoint;
    }

    public ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workItem);
        cancellationToken.ThrowIfCancellationRequested();

        _ = DeserializeCheckpointPayload(workItem);
        return ValueTask.CompletedTask;
    }

    private static RuntimeCheckpointCommandPayload DeserializeCheckpointPayload(RuntimeSchedulerWorkItem workItem)
    {
        if (workItem.Payload is not { } payload)
            throw new InvalidOperationException("Checkpoint scheduler work item requires a checkpoint payload.");

        try
        {
            return payload.Deserialize<RuntimeCheckpointCommandPayload>()
                   ?? throw new InvalidOperationException("Checkpoint scheduler work item payload resolved to null.");
        }
        catch (Exception exception) when (
            exception is JsonException or NotSupportedException ||
            exception is RuntimeCheckpointCommandPayloadValidationException)
        {
            throw new InvalidOperationException("Checkpoint scheduler work item payload is not a valid checkpoint payload.", exception);
        }
    }
}
