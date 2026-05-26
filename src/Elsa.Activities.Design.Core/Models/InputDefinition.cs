using Elsa.Primitives.Models;
namespace Elsa.Activities.Design.Core.Models;

public sealed record InputDefinition(
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
    IDictionary<string, object>? UISpecifications = null)

    : ArgumentDefinition(ReferenceKey, Name, Type, StorageDriverType, DisplayName, Category, IsBrowsable, IsSerializable, Description, Order, UiHint, PropertyInfo, UISpecifications);