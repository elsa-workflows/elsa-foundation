using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;

namespace Elsa.Activities.Design.Api.Models;

public sealed record ActivityDefinitionVersionDetailsView(
    string Id,
    int Version,
    string ImplementationKind,
    IImplementationDescriptor ImplementationDescriptor,
    ActivityDefinitionView Definition,
    IEnumerable<InputDefinition>? Inputs,
    IEnumerable<OutputDefinition>? Outputs,
    IEnumerable<ActivityPortDefinition>? Ports,
    ActivityExecutionType ExecutionType
);
