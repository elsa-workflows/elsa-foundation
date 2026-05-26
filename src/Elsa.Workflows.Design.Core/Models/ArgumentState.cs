using Elsa.Expressions.Core.Models;
using Elsa.Primitives.Models;

namespace Elsa.Workflows.Design.Core.Models;

public sealed record ArgumentState(
    string ReferenceKey,
    ArgumentValue Value,
    bool? AutoEvaluate,
    TypeInformation? EvaluatorType,
    TypeInformation? StorageDriverType,
    bool? IsSensitive
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