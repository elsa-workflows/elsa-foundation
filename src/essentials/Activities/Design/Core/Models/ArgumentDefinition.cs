using System.Text.Json;
using Elsa.Primitives.Models;

namespace Elsa.Activities.Design.Core.Models;

/// <summary>
/// Design-time canvas description of a single argument on an activity. Sibling records
/// <see cref="InputDefinition"/> and <see cref="OutputDefinition"/> carry the same structural
/// shape by FR-030 (signature-clarity duplication).
/// </summary>
/// <remarks>
/// <see cref="PropertyInfo"/> and <see cref="UISpecifications"/> are opaque, Studio-authored UI metadata
/// held as a verbatim <see cref="JsonElement"/> — never a CLR-typed <c>object</c> graph (ADR 0035 D3, amends
/// constitution §E2.9).
/// </remarks>
public sealed record ArgumentDefinition(
    string ReferenceKey,
    string Name,
    TypeReference Type,
    // Mark storage driver type for deletion from this model: it is not a design time concern, only runtime!
    string? StorageDriverType,
    string DisplayName,
    string? Category,
    bool? IsBrowsable = null,
    bool? IsSerializable = null,
    string? Description = null,
    float Order = 0,
    string? UiHint = null,
    JsonElement? PropertyInfo = null,
    JsonElement? UISpecifications = null);
