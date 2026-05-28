using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Workflows.Design.Core.Events;

/// <summary>
/// Mutation event for an output added to a placed activity's <c>Outputs</c> bag. Published by
/// <c>IAddActivityOutputToDraftCommand</c>. Per Unit C FR-018 — activity output full CRUD,
/// symmetric with workflow-output CRUD.
/// </summary>
/// <remarks>
/// Distinct from <c>OnWorkflowOutputAddedToDraft</c>, which adds a workflow-definition-level
/// output declaration. This event is per-activity binding state.
/// </remarks>
public sealed class OnActivityOutputAddedToDraft(string draftId, string nodeId, ArgumentState output) : IDomainEvent
{
    public string DraftId { get; } = draftId;
    public string NodeId { get; } = nodeId;
    public ArgumentState Output { get; } = output;
    public string OutputReferenceKey => Output.ReferenceKey;
}
