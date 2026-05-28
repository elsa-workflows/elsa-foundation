using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Mediator.Core.Contracts;

namespace Elsa.Activities.Design.Api.Commands;

public sealed record AddVersion(
    string DefinitionId,
    IImplementationDescriptor ImplementationDescriptor,
    IEnumerable<InputDefinition>? Inputs,
    IEnumerable<OutputDefinition>? Outputs,
    IEnumerable<ActivityPortDefinition>? Ports,
    ActivityKind? Kind
)
: ICommand<ActivityDefinitionVersionDetailsView>;
