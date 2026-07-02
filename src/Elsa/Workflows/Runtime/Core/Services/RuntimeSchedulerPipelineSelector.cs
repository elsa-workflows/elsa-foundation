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
        // Read only the completion-kind discriminator instead of deserializing the whole payload. Any absent property,
        // absent/non-object payload, or malformed value falls through to the routing (workflow) default, matching
        // WorkflowCompleteActivitySchedulerWorkHandler.CanHandle. Selection never throws.
        return IsParentCompletionEvaluation(workItem.Payload)
            ? RuntimePipelineKind.Activity
            : RuntimePipelineKind.Workflow;
    }

    private static bool IsParentCompletionEvaluation(JsonElement? payload)
    {
        if (payload is not { ValueKind: JsonValueKind.Object } element)
            return false;

        // Look the property up case-insensitively so both the PascalCase form produced by default serialization
        // (JsonSerializer.SerializeToElement without Web defaults) and any camelCase form are honored.
        if (!TryGetCompletionKindProperty(element, out var completionKind))
            return false;

        // The enum has no [JsonConverter], so default serialization writes the numeric ordinal. Accept both the ordinal
        // and the case-insensitive enum name so routing stays correct regardless of the serialization format in effect.
        try
        {
            return completionKind.ValueKind switch
            {
                JsonValueKind.Number => completionKind.TryGetInt32(out var ordinal)
                    && ordinal == (int)SchedulerCompletionKind.ParentCompletionEvaluation,
                JsonValueKind.String => Enum.TryParse<SchedulerCompletionKind>(completionKind.GetString(), ignoreCase: true, out var parsed)
                    && parsed == SchedulerCompletionKind.ParentCompletionEvaluation,
                _ => false
            };
        }
        catch (Exception exception) when (exception is FormatException or InvalidOperationException or ArgumentException)
        {
            return false;
        }
    }

    private static bool TryGetCompletionKindProperty(JsonElement element, out JsonElement completionKind)
    {
        const string propertyName = nameof(RuntimeCompleteActivityCommandPayload.CompletionKind);

        if (element.TryGetProperty(propertyName, out completionKind))
            return true;

        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                completionKind = property.Value;
                return true;
            }
        }

        completionKind = default;
        return false;
    }
}
