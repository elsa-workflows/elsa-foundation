using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// Maps a drained scheduler work item to its runtime execution pipeline kind, mirroring the discriminator the
/// scheduler work handlers use in <c>CanHandle</c>.
/// </summary>
public sealed class RuntimeSchedulerPipelineSelector : IRuntimeSchedulerPipelineSelector
{
    public RuntimePipelineKind Select(RuntimeSchedulerWorkItem workItem)
    {
        ArgumentNullException.ThrowIfNull(workItem);

        return workItem.CommandKind switch
        {
            // Per-activity-execution kinds run under the activity pipeline.
            WorkflowExecutionCommandKind.ScheduleActivity or
            WorkflowExecutionCommandKind.StartActivity or
            WorkflowExecutionCommandKind.InvokeActivity or
            WorkflowExecutionCommandKind.ResumeBookmark or
            WorkflowExecutionCommandKind.CreateBookmark => RuntimePipelineKind.Activity,

            // CompleteActivity is claimed by two handlers; the parent-completion evaluation step is the activity one.
            WorkflowExecutionCommandKind.CompleteActivity => SelectCompleteActivityPipeline(workItem),

            // Workflow-lifecycle kinds (Start, Checkpoint, Cancel) and any other kind reaching the drainer run under
            // the workflow pipeline. Placeholder middleware make this choice behaviorally neutral in Move 1.
            _ => RuntimePipelineKind.Workflow
        };
    }

    private static RuntimePipelineKind SelectCompleteActivityPipeline(RuntimeSchedulerWorkItem workItem)
    {
        if (workItem.Payload is not { } payload)
            return RuntimePipelineKind.Workflow;

        try
        {
            var completionPayload = payload.Deserialize<RuntimeCompleteActivityCommandPayload>();
            return completionPayload?.CompletionKind == SchedulerCompletionKind.ParentCompletionEvaluation
                ? RuntimePipelineKind.Activity
                : RuntimePipelineKind.Workflow;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or ArgumentException)
        {
            // A malformed payload cannot be a parent-completion evaluation; fall back to the routing (workflow) default,
            // matching WorkflowCompleteActivitySchedulerWorkHandler.CanHandle. Selection never throws.
            return RuntimePipelineKind.Workflow;
        }
    }
}
