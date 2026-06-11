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
    Start = 0,
    ResumeBookmark = 1,
    ContinueVolatileWait = 2,
    RunSchedulerWork = 3,
    Cancel = 4,
    PauseWorkflowExecution = 5,
    UnpauseWorkflowExecution = 6,
    ScheduleActivity = 7,
    CompleteActivity = 8,
    DeliverSignal = 9,
    CreateBookmark = 10,
    Checkpoint = 11,
    StartActivity = 12
}
