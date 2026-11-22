using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;

namespace Elsa.Activities.Design.Core.Models;

/// <summary>
/// Design-time canvas description of an activity input. Standalone sealed record by design
/// — duplicates the structural shape of <see cref="ArgumentDefinition"/> rather than inheriting,
/// keeping the input signature clear and decoupled.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="PropertyInfo"/> and <see cref="UISpecifications"/> are opaque, Studio-authored UI metadata
/// held as a verbatim <see cref="JsonElement"/> — never a CLR-typed <c>object</c> graph. Keeping them opaque
/// removes the last open-object-polymorphism dependency from the canonical StateSource.
/// </para>
/// <para>
/// <see cref="IsSensitive"/> and <see cref="IsCredential"/> carry the activity's sensitivity declaration
/// (<c>[ActivityInput(IsSensitive = true)]</c>, <c>[ActivityInput(IsCredential = true)]</c>). Each is <c>true</c> or
/// null, never <c>false</c>, and is left out of every serialized form while null, so an input that declares nothing
/// keeps the catalog content and hash it had before the flags existed.
/// </para>
/// <para>
/// <see cref="Tier"/> is the presentation-only disclosure group (<c>essential</c>, <c>optional</c>,
/// <c>setting</c>). It is left out of every serialized form while null. The catalog hasher includes
/// it when set, so adding or changing a tier re-reconciles the stored version instead of throwing
/// <c>ActivityVersionHashMismatchException</c>. JSON catalogs and the descriptor/version API set the
/// same strings; CLR authors set <c>[ActivityInput(Tier = InputTier.Optional)]</c>.
/// </para>
/// </remarks>
public sealed record InputDefinition(
    string ReferenceKey,
    string Name,
    TypeReference Type,
    string? StorageDriverType,
    string DisplayName,
    string? Category,
    [property: JsonRequired] bool IsNullable,
    bool? IsBrowsable = null,
    bool? IsSerializable = null,
    string? Description = null,
    float Order = 0,
    string? UiHint = null,
    JsonElement? PropertyInfo = null,
    JsonElement? UISpecifications = null,
    bool IsRequired = false,
    JsonElement? DefaultValue = null,
    string? DefaultSyntax = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? IsSensitive = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? IsCredential = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Tier = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ConnectionType = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? VariableAccess = null)
{
    private readonly JsonElement? _defaultValue = DefaultValue;

    /// <summary>A literal default; legacy JSON null without a syntax or with the Connection editor hint denotes no default.</summary>
    [JsonConverter(typeof(NullableJsonElementConverter))]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonElement? DefaultValue
    {
        get => _defaultValue is { ValueKind: JsonValueKind.Null } &&
               (DefaultSyntax is null || (!string.IsNullOrWhiteSpace(ConnectionType) &&
                   string.Equals(DefaultSyntax, "Connection", StringComparison.OrdinalIgnoreCase)))
            ? null
            : _defaultValue;
        init => _defaultValue = value;
    }
}

/// <summary>
/// The direction of an input that binds a variable (spec 1231 FR-022): <see cref="Read"/> for a
/// <c>ReadOnlyVariableReference&lt;T&gt;</c>, <see cref="ReadWrite"/> for a <c>VariableReference&lt;T&gt;</c>. Such an
/// input's <see cref="InputDefinition.Type"/> is the value type; its binding names a declared variable.
/// </summary>
public static class InputVariableAccess
{
    public const string Read = "Read";
    public const string ReadWrite = "ReadWrite";

    /// <summary>The authored expression type a variable-reference input accepts.</summary>
    public const string ExpressionType = "Variable";

    /// <summary>The editor hint the catalog gives a variable-reference input.</summary>
    public const string UiHint = "variable-picker";
}

/// <summary>
/// The design facet that marks an activity type <c>[AccessesUndeclaredVariables]</c> (spec 1231 FR-026): it may read
/// and write any visible variable by free name.
/// </summary>
public static class VariableAccessDesignFacet
{
    public const string Kind = "elsa.variable-access";
    public const string SchemaVersion = "1";
    public const string AccessesUndeclaredVariablesMember = "accessesUndeclaredVariables";

    /// <summary>
    /// Set, and only then present, when the activity type declares <c>[AccessesUndeclaredVariables]</c> itself rather
    /// than carrying the marker as an engine script activity (spec 1231 FR-028): its C# code may write any visible
    /// variable whatever its script body writes.
    /// </summary>
    public const string DeclaredMember = "declared";

    /// <summary>Whether <paramref name="facets"/> mark the activity as accessing undeclared variables.</summary>
    public static bool AccessesUndeclaredVariables(IEnumerable<ActivityDesignFacet>? facets) =>
        facets?.Any(facet =>
            StringComparer.Ordinal.Equals(facet.Kind, Kind) &&
            facet.Payload.ValueKind == JsonValueKind.Object &&
            facet.Payload.TryGetProperty(AccessesUndeclaredVariablesMember, out var marked) &&
            marked.ValueKind == JsonValueKind.True) == true;

    /// <summary>Whether <paramref name="facets"/> mark an activity type that declares <c>[AccessesUndeclaredVariables]</c> itself.</summary>
    public static bool DeclaresAccessesUndeclaredVariables(IEnumerable<ActivityDesignFacet>? facets) =>
        facets?.Any(facet =>
            StringComparer.Ordinal.Equals(facet.Kind, Kind) &&
            facet.Payload.ValueKind == JsonValueKind.Object &&
            facet.Payload.TryGetProperty(DeclaredMember, out var declared) &&
            declared.ValueKind == JsonValueKind.True) == true;
}
