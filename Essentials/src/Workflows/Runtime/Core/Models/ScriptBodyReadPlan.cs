using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;

namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// The body read plan of a script activity (spec 1222 FR-004, FR-033, task T049): publish parses the script input, pins
/// each variable it names literally to its nearest declaring scope (keyed <c>variables.&lt;name&gt;</c>) and records the
/// fallback marker when the script also reads variables by a computed name. Body targets are never materialized into the
/// input snapshot: the body reads them through its variable session (ADR 0090 decision 2, ADR 0092), which spec 1231
/// (S-7) builds and spec 1222 T050 serves.
/// </summary>
public sealed record ScriptBodyReadPlan
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    [JsonConstructor]
    public ScriptBodyReadPlan(
        string language,
        IReadOnlyDictionary<string, VariableExpressionParameterBinding> parameters,
        VariableReadFallback? variableReadFallback = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(language);
        ArgumentNullException.ThrowIfNull(parameters);
        ExpressionParameterSurfaces.Validate(
            parameters.ToDictionary(item => item.Key, item => (ExpressionParameterBinding)item.Value, StringComparer.Ordinal),
            nameof(parameters));
        Language = language;
        Parameters = parameters
            .OrderBy(item => item.Key, StringComparer.Ordinal)
            .ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        VariableReadFallback = variableReadFallback;
    }

    public string Language { get; }

    /// <summary>The pinned body reads, keyed <c>variables.&lt;name&gt;</c>.</summary>
    public IReadOnlyDictionary<string, VariableExpressionParameterBinding> Parameters { get; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public VariableReadFallback? VariableReadFallback { get; }

    public string Serialize() => JsonSerializer.Serialize(this, SerializerOptions);

    /// <summary>The plan pinned on <paramref name="node"/>, or <see langword="null"/> when its body reads no variable.</summary>
    public static ScriptBodyReadPlan? Of(ExecutableNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return node.Metadata.TryGetValue(RuntimeMetadataKeys.ScriptBodyReadPlan, out var json)
            ? JsonSerializer.Deserialize<ScriptBodyReadPlan>(json, SerializerOptions)
            : null;
    }
}
