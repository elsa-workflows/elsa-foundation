using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;

namespace Elsa3.Mapping.Models;

public sealed record ActivityDefinitionVersionImport(
    string Id,
    int Version,
    string DefinitionId,
    string ActivityTypeKey,
    string ImplementationKind,
    IImplementationDescriptor ImplementationDescriptor,
    IActivityDefinition Definition,
    IEnumerable<InputDefinition> Inputs,
    IEnumerable<OutputDefinition> Outputs,
    IEnumerable<ActivityPortDefinition> Ports,
    ActivityExecutionType ExecutionType
)
: IActivityDefinitionVersion;
