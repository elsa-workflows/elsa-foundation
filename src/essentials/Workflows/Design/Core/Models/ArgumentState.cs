using System.Text.Json;
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

    /// <summary>
    /// True when the argument carries a value. A missing or null value, a JSON null or undefined element and an empty
    /// string leave the input unbound. The design-time required-input check and publication's handling of an
    /// encryption-required input share this one definition.
    /// </summary>
    public bool IsBound() => Value?.Value switch
    {
        null => false,
        JsonElement { ValueKind: JsonValueKind.Undefined } => false,
        JsonElement { ValueKind: JsonValueKind.Null } => false,
        string value => !string.IsNullOrEmpty(value),
        JsonElement { ValueKind: JsonValueKind.String } value => !string.IsNullOrEmpty(value.GetString()),
        _ => true
    };
}
