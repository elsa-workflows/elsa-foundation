using Elsa.Primitives.Models;

namespace Elsa.Workflows.Design.Core.Models;

public sealed record VariableDefinition(
    string ReferenceKey,
    string Name,
    TypeInformation TypeInformation,
    TypeInformation StorageDriverType,
    ArgumentValue Default
);
