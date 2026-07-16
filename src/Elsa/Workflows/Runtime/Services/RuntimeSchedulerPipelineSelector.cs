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
            WorkflowExecutionCommandKind.CreateBookmark or
            WorkflowExecutionCommandKind.CancelActivityScope or
            WorkflowExecutionCommandKind.RetryActivityBoundary => RuntimePipelineKind.Activity,

            // CompleteActivity is claimed by two handlers; the parent-completion evaluation step is the activity one.
            WorkflowExecutionCommandKind.CompleteActivity => SelectCompleteActivityPipeline(workItem),

            // Workflow-lifecycle kinds (Start, Checkpoint, Cancel) and any other kind reaching the drainer run under
            // the workflow pipeline. Placeholder middleware make this choice behaviorally neutral in Move 1.
            _ => RuntimePipelineKind.Workflow
        };
    }

    private static RuntimePipelineKind SelectCompleteActivityPipeline(RuntimeSchedulerWorkItem workItem)
    {
        // Fast path: if the discriminator is not parent-completion, the item is workflow routing work — decide from the
        // cheap property read without deserializing the whole payload (absent/non-object payload → workflow too).
        if (!ReadsAsParentCompletionKind(workItem.Payload))
            return RuntimePipelineKind.Workflow;

        // The discriminator reads as parent-completion; confirm with the *same* full deserialize the parent-completion
        // handler's CanHandle performs. A payload that reads as parent-completion but fails validation is claimed by the
        // workflow routing handler (whose CanHandle catches the same throw and returns true), so route it to workflow to
        // keep selection in agreement with the handler that actually runs.
        return DeserializesAsParentCompletion(workItem)
            ? RuntimePipelineKind.Activity
            : RuntimePipelineKind.Workflow;
    }

    private static bool DeserializesAsParentCompletion(RuntimeSchedulerWorkItem workItem)
    {
        if (workItem.Payload is null)
            return false;

        try
        {
            // RT-11: reuse the single per-work-item parse instead of deserializing the payload again here.
            return RuntimeCompleteActivityPayloadMemo.Deserialize(workItem)?.CompletionKind == SchedulerCompletionKind.ParentCompletionEvaluation;
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or ArgumentException)
        {
            return false;
        }
    }

    private static bool ReadsAsParentCompletionKind(JsonElement? payload)
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
