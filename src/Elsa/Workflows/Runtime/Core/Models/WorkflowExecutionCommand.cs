using System.Text.Json;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// Mailbox command for one workflow execution agent.
/// </summary>
public sealed record WorkflowExecutionCommand(
    string CommandId,
    string WorkflowExecutionId,
    WorkflowExecutionCommandKind Kind,
    DateTimeOffset EnqueuedAt,
    JsonElement? Payload,
    IReadOnlyDictionary<string, string> Metadata);

public enum WorkflowExecutionCommandKind
{
    Start,
    ResumeBookmark,
    ContinueVolatileWait,
    RunSchedulerWork,
    Cancel,
    PauseWorkflowExecution,
    UnpauseWorkflowExecution,
    ScheduleActivity,
    CompleteActivity,
    DeliverSignal,
    CreateBookmark,
    Checkpoint
}
