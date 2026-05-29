using Elsa.Mediator.Core.Contracts;
using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Workflows.Design.Core.Events;

/// <summary>
/// Mutation event for an input added to a placed activity's <c>Inputs</c> bag. Published by
/// <c>IAddActivityInputToDraftCommand</c>. Per Unit C FR-018 â€” activity input full CRUD,
/// symmetric with workflow-input CRUD and variable CRUD.
/// </summary>
/// <remarks>
/// Distinct from <c>OnWorkflowInputAddedToDraft</c>, which adds a workflow-definition-level
/// input declaration. This event is per-activity binding state.
/// </remarks>
public sealed class OnActivityInputAddedToDraft(string draftId, string nodeId, ArgumentState input) : ILifecycleEvent
{
    public string DraftId { get; } = draftId;
    public string NodeId { get; } = nodeId;
    public ArgumentState Input { get; } = input;
    public string InputReferenceKey => Input.ReferenceKey;
}
