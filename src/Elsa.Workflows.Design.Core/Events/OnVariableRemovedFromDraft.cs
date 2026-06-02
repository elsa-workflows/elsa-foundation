using Elsa.Events.Core.Contracts;

namespace Elsa.Workflows.Design.Core.Events;

/// <summary>
/// Mutation event for a workflow variable removed from the Draft. Published by
/// <c>IRemoveVariableFromDraftCommand</c>. Per Unit C FR-018 variables (definition-bag) bullet.
/// </summary>
public sealed class OnVariableRemovedFromDraft(string draftId, string variableReferenceKey) : IEvent
{
    public string DraftId { get; } = draftId;
    public string VariableReferenceKey { get; } = variableReferenceKey;
}
