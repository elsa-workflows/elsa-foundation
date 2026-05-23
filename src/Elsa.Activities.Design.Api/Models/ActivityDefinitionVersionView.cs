using Elsa.Activities.Design.Core.Models;
using Elsa.Primitives.Models;

namespace Elsa.Activities.Design.Api.Models;

public sealed record ActivityDefinitionVersionView(
    string Id,
    int Version, 
    TypeInformation TypeInfo,
    ActivityDefinitionView Definition, 
    IEnumerable<InputDefinition>? Inputs, 
    IEnumerable<OutputDefinition>? Outputs, 
    IEnumerable<ActivityPortDefinition>? Ports, 
    ActivityKind Kind
);
