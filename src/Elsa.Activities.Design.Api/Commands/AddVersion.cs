using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Core.Models;
using Elsa.Mediator.Core.Contracts;
using Elsa.Primitives.Models;

namespace Elsa.Activities.Design.Api.Commands;

public sealed record AddVersion(
    TypeInformation TypeInfo, 
    string DefinitionId, 
    IEnumerable<InputDefinition>? Inputs, 
    IEnumerable<OutputDefinition>? Outputs, 
    IEnumerable<ActivityPortDefinition>? Ports, 
    ActivityKind? Kind
)
: ICommand<ActivityDefinitionVersionView>;
