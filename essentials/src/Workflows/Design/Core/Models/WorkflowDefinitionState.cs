using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Activities.Design.Core.Models;
using Elsa.Expressions.Core.Models;

namespace Elsa.Workflows.Design.Core.Models;

/// <summary>
/// The canonical authored document of a workflow definition. Persisted as the
/// <c>StateSource</c> shadow JSON on <c>WorkflowDefinitionVersion</c> (immutable) and
/// <c>WorkflowDefinitionDraft</c> (mutable) inside the <c>Elsa.Workflows.Design</c>
/// sub-domain.
/// </summary>
/// <remarks>
/// <para>
/// <b>Scope.</b> State carries <b>authored content only</b>:
/// the structured representation of what the author drew, declared, and configured.
/// Today's members — <c>Variables</c>, <c>RootActivity</c>, <c>Inputs</c>,
/// <c>Outputs</c>, and <c>StrategyOptions</c> — are clean
/// against the policy. The root activity owns any composition details such as Flowchart
/// connections or Sequence children.
/// </para>
/// <para>
/// <b>Out of State (never add to this record):</b>
/// instance/runtime/operational state · executable/build metadata · publication/deployment
/// state · search/listing-projection types · security/ownership types · designer layout
/// metadata (owned by <c>WorkflowDefinitionVersionLayout</c> / <c>WorkflowDefinitionDraftLayout</c>
/// siblings via <c>IWorkflowDefinitionLayout</c>) · validation errors (derived state, produced by
/// the in-lock validation gate; not persisted).
/// </para>
/// <para>
/// <b>Architectural triplet.</b> <c>WorkflowDefinitionState</c> ↔ read
/// models/projections ↔ <c>WorkflowExecutable</c> — authoring, reading, executing — at
/// separate scopes; never merged. Conflation collapses the Design ↔ Runtime split
/// or the artifact-only runtime.
/// </para>
/// <para>
/// <b>Enforcement.</b> This documentation header + PR review discipline against
/// this scope. A property newly proposed for State whose category is genuinely ambiguous between
/// authored content and an out-of-State category surfaces as an architecture-meeting
/// escalation; resolution is constitutional, not silent. Automated
/// compile-/build-time enforcement is deferred to a future <i>Code Analysers</i> epic.
/// </para>
/// </remarks>
public sealed record WorkflowDefinitionState(
    IEnumerable<VariableDefinition> Variables,
    ActivityNode? RootActivity,
    IEnumerable<InputDefinition> Inputs,
    IEnumerable<OutputDefinition> Outputs,
    WorkflowStrategyOptions? StrategyOptions
)
{
    /// <summary>Workflow nullability comes from its type; the shared descriptor flag belongs to CLR catalogs.</summary>
    private IEnumerable<InputDefinition> _inputs = (Inputs ?? []).Select(input => input with { IsNullable = false }).ToArray();
    private IEnumerable<OutputDefinition> _outputs = (Outputs ?? []).Select(output => output with { IsNullable = false }).ToArray();
    [JsonConverter(typeof(AuthoredMemberListJsonConverter<InputDefinition>))]
    public IEnumerable<InputDefinition> Inputs
    {
        get => _inputs;
        init => _inputs = (value ?? []).Select(input => input with { IsNullable = false }).ToArray();
    }
    [JsonConverter(typeof(AuthoredMemberListJsonConverter<OutputDefinition>))]
    public IEnumerable<OutputDefinition> Outputs
    {
        get => _outputs;
        init => _outputs = (value ?? []).Select(output => output with { IsNullable = false }).ToArray();
    }

    /// <summary>
    /// The fallback used when a persisted state source is blank or fails to deserialize, so
    /// callers always get a valid (if empty) state rather than a null reference.
    /// </summary>
    public static WorkflowDefinitionState Empty { get; } = new([], null, [], [], null);
}

/// <summary>The authored workflow wire omits the CLR descriptor's independent nullable flag.</summary>
internal sealed class AuthoredMemberListJsonConverter<T> : JsonConverter<IEnumerable<T>>
{
    public AuthoredMemberListJsonConverter() { }
    public override IEnumerable<T>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        JsonSerializer.Deserialize<List<T>>(ref reader, options);

    public override void Write(Utf8JsonWriter writer, IEnumerable<T> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var member in value)
        {
            writer.WriteStartObject();
            foreach (var property in JsonSerializer.SerializeToElement(member, options).EnumerateObject())
                if (!StringComparer.OrdinalIgnoreCase.Equals(property.Name, "isNullable")) property.WriteTo(writer);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }
}
