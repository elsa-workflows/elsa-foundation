using Elsa.Activities.Bpmn.Interchange.Models;

namespace Elsa.Activities.Bpmn.Interchange.Contracts;

/// <summary>Chooses the Foundation activity performing one declared unit of BPMN work.</summary>
/// <remarks>
/// Scoped single-implementation replacement contract. A host may register one binding policy before
/// the feature; shell startup rejects competing registrations. This maps host work, never token semantics.
/// </remarks>
public interface IBpmnWorkBinder
{
    BpmnWorkBindingResult Bind(BpmnWorkBindingRequest request);
}
