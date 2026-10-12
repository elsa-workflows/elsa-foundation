using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Serialization.Core.Exceptions;

namespace Elsa.Expressions.Services;

/// <inheritdoc />
/// <remarks>
/// Aggregates every registered <see cref="IVariableTypeDescriptorProvider"/> once at construction
/// (mirrors <see cref="ExpressionDescriptorRegistry"/>). Two providers contributing the same
/// <see cref="TypeDescriptor.Alias"/> are a configuration error and fail fast with
/// <see cref="DuplicateTypeAliasException"/>, consistent with the runtime registry's fail-fast policy.
/// </remarks>
public sealed class VariableTypeDescriptorCatalog : IVariableTypeDescriptorCatalog
{
    private readonly IDictionary<string, TypeDescriptor> _descriptors = new Dictionary<string, TypeDescriptor>();

    /// <summary>
    /// Aggregates the union of descriptors contributed by every registered provider, keyed by alias.
    /// </summary>
    public VariableTypeDescriptorCatalog(IEnumerable<IVariableTypeDescriptorProvider> providers)
    {
        foreach (var provider in providers)
        foreach (var descriptor in provider.GetDescriptors())
        {
            if (_descriptors.ContainsKey(descriptor.Alias))
                throw new DuplicateTypeAliasException(descriptor.Alias);

            _descriptors[descriptor.Alias] = descriptor;
        }
    }

    /// <inheritdoc />
    public IEnumerable<TypeDescriptor> GetDescriptors() => _descriptors.Values;

    /// <inheritdoc />
    public IEnumerable<IGrouping<string, TypeDescriptor>> GetDescriptorsByCategory() =>
        _descriptors.Values.GroupBy(x => x.Category);
}
