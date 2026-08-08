using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Primitives.Models;

namespace Elsa.Activities.Design.Core.Models;

/// <summary>
/// Design-time canvas description of an activity input. Standalone sealed record by FR-030
/// — duplicates the structural shape of <see cref="ArgumentDefinition"/> rather than inheriting,
/// keeping the input signature clear and decoupled.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="PropertyInfo"/> and <see cref="UISpecifications"/> are opaque, Studio-authored UI metadata
/// held as a verbatim <see cref="JsonElement"/> — never a CLR-typed <c>object</c> graph. Keeping them opaque
/// removes the last open-object-polymorphism dependency from the canonical StateSource (ADR 0035 D3, amends
/// constitution §E2.9).
/// </para>
/// <para>
/// <see cref="EnumValues"/> lists the accepted members of an enum-typed input in wire spelling, and is
/// <see langword="null"/> for every other type. Publishing the type name and a default without the member
/// list forced consumers to guess the remaining values — a measured cost: one guess ("blocking") was
/// rejected at publish, another ("sync") silently changed response behaviour.
/// </para>
/// <para>
/// <see cref="HasStaticDefault"/> disambiguates <see cref="DefaultValue"/> (G1, spec 149 / RFC #1191):
/// <c>true</c> with a value = concrete static default; <c>true</c> with <c>null</c> = the default is null
/// (unauthored input yields null); <c>false</c> = no statically representable default exists (computed or
/// conditional initializer). Rows persisted before G1 deserialize as <c>false</c>; a non-null
/// <see cref="DefaultValue"/> implies a static default regardless.
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
    bool HasStaticDefault = false,
    IReadOnlyList<string>? EnumValues = null);
