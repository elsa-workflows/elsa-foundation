using Elsa.Expressions.Core.Models;
using Elsa.Mediator.Core.Contracts;

namespace Elsa.Workflows.Design.Core.Events;

/// <summary>
/// Mutation event for a workflow variable updated on the Draft. Published by
/// <c>IUpdateVariableInDraftCommand</c>. <see cref="VariableReferenceKey"/> is the stable
/// identity per FR-010 (argument/variable-level <c>ReferenceKey</c> is unchanged in Unit C).
/// </summary>
public sealed class OnVariableUpdatedInDraft(
    string draftId,
    string variableReferenceKey,
    VariableDefinition oldValue,
    VariableDefinition newValue) : IDomainEvent
{
    public string DraftId { get; } = draftId;
    public string VariableReferenceKey { get; } = variableReferenceKey;
    public VariableDefinition OldValue { get; } = oldValue;
    public VariableDefinition NewValue { get; } = newValue;
}
