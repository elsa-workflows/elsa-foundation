using Elsa.Primitives.Models;

namespace Elsa.Activities.Design.Core.Contracts;

public interface IArgumentDefinition
{
    string ReferenceKey { get; }

    TypeInformation Type { get; }

    TypeInformation? StorageDriverType { get; }

    /// <summary>
    /// The name.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// The user friendly name of the input. Used by UI tools.
    /// </summary>
    string? DisplayName { get; }

    /// <summary>
    /// The user friendly description of the input. Used by UI tools.
    /// </summary>
    string? Description { get; }

    /// <summary>
    /// The order in which this input should be displayed by UI tools.
    /// </summary>
    float Order { get; }

    /// <summary>
    /// True if this property should be displayed by UI tools, false otherwise.
    /// </summary>
    bool IsBrowsable { get; }

    /// <summary>
    /// True if this property can be serialized.
    /// </summary>
    bool IsSerializable { get; }

    /// <summary>
    /// The category to which this input belongs within the activity
    /// </summary>
    string? Category { get; }

    /// <summary>
    /// Additional text displayed by UI tools to provide hints about how to use this property.
    /// </summary>
    string? UiHint { get; }

    /// <summary>
    /// 
    /// </summary>
    IDictionary<string, object>? PropertyInfo { get; }

    /// <summary>
    /// 
    /// </summary>
    IDictionary<string, object>? UISpecifications { get; }
}
