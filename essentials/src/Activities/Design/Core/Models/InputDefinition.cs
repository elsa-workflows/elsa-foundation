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
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ConnectionType = null)
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
