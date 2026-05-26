using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Primitives.Models;

namespace Elsa3.Mapping.Models;

public sealed record ActivityDefinitionVersionImport(
    string Id,
    int Version,
    TypeInformation TypeInfo,
    IActivityDefinition Definition,
    IEnumerable<InputDefinition> Inputs,
    IEnumerable<OutputDefinition> Outputs,
    IEnumerable<ActivityPortDefinition> Ports,
    ActivityKind Kind
)
: IActivityDefinitionVersion;