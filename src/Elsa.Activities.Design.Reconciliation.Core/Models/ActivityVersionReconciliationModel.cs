using Elsa.Activities.Design.Core.Models;

namespace Elsa.Activities.Design.Reconciliation.Core.Models;

public sealed record ActivityVersionReconciliationModel(
    string? Id,
    string Version,
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
