using Elsa.Activities.DispatchWorkflow.Runtime.Constants;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Activities.DispatchWorkflow.Runtime.Models;

/// <summary>Replay-stable, payload-safe work required to resume one waiting DispatchWorkflow activity.</summary>
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
        DispatchWorkflowResult result)
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

        DispatchId = dispatchId;
        ParentWorkflowExecutionId = parentWorkflowExecutionId;
        ParentActivityExecutionId = parentActivityExecutionId;
        ChildWorkflowExecutionId = childWorkflowExecutionId;
        BookmarkId = bookmarkId;
        StimulusType = stimulusType;
        StimulusHash = stimulusHash;
        Result = result;
    }

    public string DispatchId { get; }
    public string ParentWorkflowExecutionId { get; }
    public string ParentActivityExecutionId { get; }
    public string ChildWorkflowExecutionId { get; }
    public string BookmarkId { get; }
    public string StimulusType { get; }
    public string StimulusHash { get; }
    public DispatchWorkflowResult Result { get; }
}
