using Elsa.Primitives.Models;

namespace Elsa.Activities.Design.Core.Models;

/// <summary>
/// Design-time canvas description of an activity output. Standalone sealed record by FR-030
/// — duplicates the structural shape of <see cref="ArgumentDefinition"/> rather than inheriting,
/// keeping the output signature clear and decoupled.
/// </summary>
public sealed record OutputDefinition(
    string ReferenceKey,
    string Name,
    TypeInformation Type,
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
