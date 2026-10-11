using Elsa.Expressions.Core.Models;

namespace Elsa.Expressions.Core.Contracts;

/// <summary>
/// The aggregated, design-time catalog of selectable argument element types — the union of every
/// registered <see cref="IVariableTypeDescriptorProvider"/>, keyed by alias and groupable by category
/// for a grouped type picker (FR-016). Distinct from the runtime resolution authority
/// (<c>IWellKnownTypeRegistry</c>), though both are keyed by the same alias.
/// </summary>
public interface IVariableTypeDescriptorCatalog
{
    /// <summary>Returns the union of all contributed descriptors, keyed by alias.</summary>
    IEnumerable<TypeDescriptor> GetDescriptors();

    /// <summary>Returns the contributed descriptors grouped by <see cref="TypeDescriptor.Category"/>.</summary>
    IEnumerable<IGrouping<string, TypeDescriptor>> GetDescriptorsByCategory();
}
