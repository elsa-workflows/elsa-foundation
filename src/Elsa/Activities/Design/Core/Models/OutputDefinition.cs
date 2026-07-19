using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Primitives.Models;

namespace Elsa.Activities.Design.Core.Models;

/// <summary>
/// Design-time canvas description of an activity output. Standalone sealed record by FR-030
/// — duplicates the structural shape of <see cref="ArgumentDefinition"/> rather than inheriting,
/// keeping the output signature clear and decoupled.
/// </summary>
/// <remarks>
/// <see cref="PropertyInfo"/> and <see cref="UISpecifications"/> are opaque, Studio-authored UI metadata
/// held as a verbatim <see cref="JsonElement"/> — never a CLR-typed <c>object</c> graph (ADR 0035 D3, amends
/// constitution §E2.9).
/// </remarks>
public sealed record OutputDefinition(
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
    ValueRepresentation? SourceRepresentation = null);
