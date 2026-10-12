using Elsa.Activities.Design.Core.Models;
using Elsa.Events.Core.Contracts;

namespace Elsa.Workflows.Design.Core.Events;

/// <summary>
/// Mutation event for a workflow-definition-level input updated on the Draft. Published by
/// <c>IUpdateWorkflowInputInDraftCommand</c>. <see cref="InputReferenceKey"/> is the stable
/// identity per FR-010.
/// </summary>
public sealed class WorkflowInputUpdatedInDraft(
    string draftId,
    string inputReferenceKey,
    InputDefinition oldValue,
    InputDefinition newValue) : IEvent
{
    public string DraftId { get; } = draftId;
    public string InputReferenceKey { get; } = inputReferenceKey;
    public InputDefinition OldValue { get; } = oldValue;
    public InputDefinition NewValue { get; } = newValue;
}
