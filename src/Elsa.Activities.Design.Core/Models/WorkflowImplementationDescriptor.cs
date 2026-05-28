using Elsa.Activities.Design.Core.Contracts;

namespace Elsa.Activities.Design.Core.Models;

/// <summary>
/// Descriptor for activity versions whose implementation is a workflow definition.
/// Carries the workflow definition + version identifiers. Round-trip proof in Unit B —
/// the matching resolver lives in Unit G (workflow-as-activity bridge); attempting to
/// construct one through <c>IActivityFactory</c> from Unit B alone fails with
/// <c>ActivityResolutionException</c>.
/// </summary>
public sealed record WorkflowImplementationDescriptor(
    string WorkflowDefinitionId,
    int WorkflowVersionId)
    : IImplementationDescriptor
{
    public string Kind => "Workflow";
}
