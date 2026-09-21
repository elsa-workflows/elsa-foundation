using Elsa.Expressions.Core.Models;

namespace Elsa.Workflows.Design.Core.Models;

public sealed record ArgumentState(
    string ReferenceKey,
    ArgumentValue Value,
    bool? AutoEvaluate,
    // Stable type aliases (the shared TypeAliasConvention), never an assembly-qualified name.
    string? EvaluatorType,
    string? StorageDriverType,
    bool? IsSensitive,
    AuthoredValueConversionRequest? Conversion = null
)
{
    public static ArgumentState Null(string refKey) => new(
        refKey,
        new ArgumentValue(null),
        null,
        null,
        null,
        null
    );
}
