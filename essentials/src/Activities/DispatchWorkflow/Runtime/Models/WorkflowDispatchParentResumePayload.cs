using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Activities.DispatchWorkflow.Runtime.Constants;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.DispatchWorkflow.Runtime.Models;

/// <summary>
/// Replay-stable work required to resume one waiting DispatchWorkflow activity. It carries the terminal result (status,
/// allow-listed diagnostics, child control) and, for a completed child only, the child's committed output values
/// (<see cref="Output"/>) without any output whose value protection policy forbids carrying it, plus the names of the
/// outputs left out (<see cref="WithheldOutputs"/>), never their values (spec 1245 FR-003).
/// </summary>
public sealed class WorkflowDispatchParentResumePayload
{
    public WorkflowDispatchParentResumePayload(
        string dispatchId,
        string parentWorkflowExecutionId,
        string parentActivityExecutionId,
        string childWorkflowExecutionId,
        string bookmarkId,
        string stimulusType,
        string stimulusHash,
        DispatchWorkflowResult result,
        JsonElement? output,
        IReadOnlyList<string>? withheldOutputs)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dispatchId);
        ArgumentException.ThrowIfNullOrWhiteSpace(parentWorkflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(parentActivityExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(childWorkflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(bookmarkId);
        ArgumentException.ThrowIfNullOrWhiteSpace(stimulusType);
        ArgumentException.ThrowIfNullOrWhiteSpace(stimulusHash);
        ArgumentNullException.ThrowIfNull(result);

        var identity = new WorkflowDispatchIdentity(parentWorkflowExecutionId, parentActivityExecutionId);
        if (!StringComparer.Ordinal.Equals(dispatchId, identity.DispatchId) ||
            !StringComparer.Ordinal.Equals(childWorkflowExecutionId, identity.ChildWorkflowExecutionId) ||
            !StringComparer.Ordinal.Equals(bookmarkId, identity.WaitBookmarkId) ||
            !StringComparer.Ordinal.Equals(stimulusType, DispatchWorkflowConstants.WaitStimulusType) ||
            !StringComparer.Ordinal.Equals(stimulusHash, identity.WaitStimulusHash) ||
            !StringComparer.Ordinal.Equals(result.ChildWorkflowExecutionId, childWorkflowExecutionId) ||
            !DispatchWorkflowResult.SupportsParentResume(result.Status))
        {
            throw new ArgumentException("DispatchWorkflow parent-resume payload does not match its deterministic terminal dispatch identity.");
        }

        if (result.Status == WorkflowDispatchStatus.Completed)
        {
            if (output is not { ValueKind: JsonValueKind.Object } || withheldOutputs is null)
                throw new ArgumentException("A completed child's parent-resume payload requires its output object and withheld output names.", nameof(output));
            if (withheldOutputs.Any(string.IsNullOrWhiteSpace))
                throw new ArgumentException("A withheld output name must be nonblank.", nameof(withheldOutputs));
        }
        else if (output is not null || withheldOutputs is not null)
        {
            throw new ArgumentException("Only a completed child's parent-resume payload may carry child outputs.", nameof(output));
        }

        DispatchId = dispatchId;
        ParentWorkflowExecutionId = parentWorkflowExecutionId;
        ParentActivityExecutionId = parentActivityExecutionId;
        ChildWorkflowExecutionId = childWorkflowExecutionId;
        BookmarkId = bookmarkId;
        StimulusType = stimulusType;
        StimulusHash = stimulusHash;
        Result = result;
        Output = output?.Clone();
        WithheldOutputs = withheldOutputs?.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    public string DispatchId { get; }
    public string ParentWorkflowExecutionId { get; }
    public string ParentActivityExecutionId { get; }
    public string ChildWorkflowExecutionId { get; }
    public string BookmarkId { get; }
    public string StimulusType { get; }
    public string StimulusHash { get; }
    public DispatchWorkflowResult Result { get; }

    /// <summary>The completed child's committed output values, name to value in ordinal order; null unless completed.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? Output { get; }

    /// <summary>Names of the child outputs left out of <see cref="Output"/>, ordinal; empty when none, null unless completed.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? WithheldOutputs { get; }
}
