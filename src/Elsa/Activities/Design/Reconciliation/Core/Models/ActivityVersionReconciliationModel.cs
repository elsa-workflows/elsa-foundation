using Elsa.Activities.Design.Core.Models;

namespace Elsa.Activities.Design.Reconciliation.Core.Models;

public sealed record ActivityVersionReconciliationModel(
    string? Id,
    string Version,
    string ActivityTypeKey,
    string? DisplayName,
    string? Category,
    string? Description,
    string DescriptorType,
    object Descriptor,
    IEnumerable<InputDefinition> Inputs,
    IEnumerable<OutputDefinition> Outputs,
    IEnumerable<ActivityDesignFacet> DesignFacets,
    ActivityExecutionType ExecutionType = ActivityExecutionType.Action
);
