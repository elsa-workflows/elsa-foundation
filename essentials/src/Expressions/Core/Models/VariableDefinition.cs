using Elsa.Primitives.Models;

namespace Elsa.Expressions.Core.Models;

public sealed record VariableDefinition(
    string ReferenceKey,
    string Name,
    TypeReference Type,
    string? StorageDriverType,
    ArgumentValue? Default
);