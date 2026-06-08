using Elsa.Primitives.Models;

namespace Elsa.Activities.Design.Core.Models;

/// <summary>
/// Design-time canvas description of a single argument on an activity. Sibling records
/// <see cref="InputDefinition"/> and <see cref="OutputDefinition"/> carry the same structural
/// shape by FR-030 (signature-clarity duplication).
/// </summary>
public sealed record ArgumentDefinition(
    string ReferenceKey,
    string Name,
    TypeInformation Type,
    // Mark storage driver type for deletion from this model: it is not a design time concern, only runtime!
    TypeInformation? StorageDriverType,
    string DisplayName,
    string? Category,
    bool? IsBrowsable = null,
    bool? IsSerializable = null,
    string? Description = null,
    float Order = 0,
    string? UiHint = null,
    IDictionary<string, object>? PropertyInfo = null,
    IDictionary<string, object>? UISpecifications = null);
