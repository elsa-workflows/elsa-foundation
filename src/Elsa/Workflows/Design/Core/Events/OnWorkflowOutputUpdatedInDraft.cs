using Elsa.Activities.Design.Core.Models;
using Elsa.Events.Core.Contracts;

namespace Elsa.Workflows.Design.Core.Events;

/// <summary>
/// Mutation event for a workflow-definition-level output updated on the Draft. Published by
/// <c>IUpdateWorkflowOutputInDraftCommand</c>. <see cref="OutputReferenceKey"/> is the stable
/// identity per FR-010.
/// </summary>
public sealed class OnWorkflowOutputUpdatedInDraft(
    string draftId,
    string outputReferenceKey,
    OutputDefinition oldValue,
    OutputDefinition newValue) : IEvent
{
    public string DraftId { get; } = draftId;
    public string OutputReferenceKey { get; } = outputReferenceKey;
    public OutputDefinition OldValue { get; } = oldValue;
    public OutputDefinition NewValue { get; } = newValue;
}
