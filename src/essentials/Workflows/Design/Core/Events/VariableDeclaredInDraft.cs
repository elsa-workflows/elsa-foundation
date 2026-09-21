using Elsa.Expressions.Core.Models;
using Elsa.Events.Core.Contracts;

namespace Elsa.Workflows.Design.Core.Events;

/// <summary>
/// Mutation event for a workflow variable declared on the Draft. Published by
/// <c>IUpdateDraftCommand</c> as a per-diff emission (Unit 2) when a desired variable
/// <c>ReferenceKey</c> is absent from stored. Per Unit C FR-018 variables (definition-bag) bullet.
/// </summary>
public sealed class VariableDeclaredInDraft(string draftId, VariableDefinition variable) : IEvent
{
    public string DraftId { get; } = draftId;
    public VariableDefinition Variable { get; } = variable;
}
