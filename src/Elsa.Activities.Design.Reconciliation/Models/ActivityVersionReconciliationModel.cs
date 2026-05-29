using Elsa.Activities.Design.Core.Models;

namespace Elsa.Activities.Design.Reconciliation.Models;

public sealed record ActivityVersionReconciliationModel(
    string? Id,
    int Version,
    string ActivityTypeKey,
    string? DisplayName,
    string? Category,
    string? Description,
    string ImplementationKind,
    object ImplementationDescriptor,
    IEnumerable<InputDefinition> Inputs,
    IEnumerable<OutputDefinition> Outputs,
    IEnumerable<ActivityPortDefinition> Ports,
    ActivityExecutionType ExecutionType = ActivityExecutionType.Action
);
