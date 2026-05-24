using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Core.Models;
using Elsa.Mediator.Core.Contracts;
using Elsa.Primitives.Models;

namespace Elsa.Activities.Design.Api.Commands;

public sealed record AddDefinition(
    string UniqueName, 
    TypeInformation TypeInfo, 
    string Category, 
    string DisplayName, 
    string? Description = null, 
    bool? IsBrowsable = null, 
    ActivityKind? Kind = null, 
    IEnumerable<InputDefinition>? Inputs = null, 
    IEnumerable<OutputDefinition>? Outputs = null, 
    IEnumerable<ActivityPortDefinition>? Ports = null
) 

: ICommand<ActivityDefinitionVersionDetailsView>;
