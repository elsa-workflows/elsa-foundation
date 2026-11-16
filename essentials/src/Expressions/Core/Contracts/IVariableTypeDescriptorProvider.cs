using Elsa.Expressions.Core.Models;

namespace Elsa.Expressions.Core.Contracts;

/// <summary>
/// A module-contributed source of selectable argument element types. Each provider yields one or more
/// <see cref="TypeDescriptor"/>s; the aggregating <see cref="IVariableTypeDescriptorCatalog"/> unions all
/// registered providers. Register via <c>services.AddSingleton&lt;IVariableTypeDescriptorProvider, MyProvider&gt;()</c>.
/// </summary>
public interface IVariableTypeDescriptorProvider
{
    /// <summary>Returns the type descriptors this provider contributes.</summary>
    IEnumerable<TypeDescriptor> GetDescriptors();
}
