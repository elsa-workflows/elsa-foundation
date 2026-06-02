using Elsa.Expressions.Core.Models;
using Elsa.Events.Core.Contracts;

namespace Elsa.Workflows.Design.Core.Events;

/// <summary>
/// Mutation event for a workflow variable declared on the Draft. Published by
/// <c>IDeclareVariableInDraftCommand</c>. Per Unit C FR-018 variables (definition-bag) bullet.
/// </summary>
public sealed class OnVariableDeclaredInDraft(string draftId, VariableDefinition variable) : IEvent
{
    public string DraftId { get; } = draftId;
    public VariableDefinition Variable { get; } = variable;
}
