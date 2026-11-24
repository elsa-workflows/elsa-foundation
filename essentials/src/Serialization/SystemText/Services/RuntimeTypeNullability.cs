using Elsa.Expressions.Core.Contracts;
using Elsa.Serialization.Core;
using Elsa.Serialization.Core.Exceptions;

namespace Elsa.Serialization.SystemText.Services;

/// <summary>Builds the runtime contract from shared contributions without dispatching the design catalog.</summary>
internal sealed class RuntimeTypeNullability : IRuntimeTypeNullability
{
    private readonly Dictionary<string, (Type ClrType, bool SupportsNull)> _contracts = new(StringComparer.OrdinalIgnoreCase);
    private readonly IWellKnownTypeRegistry _types;

    public RuntimeTypeNullability(IEnumerable<IVariableTypeDescriptorProvider> providers, IWellKnownTypeRegistry types)
    {
        _types = types;
        foreach (var descriptor in providers.SelectMany(provider => provider.GetDescriptors()))
        {
            if (_contracts.TryGetValue(descriptor.Alias, out var existing) && existing != (descriptor.ClrType, descriptor.SupportsNull))
                throw new DuplicateTypeAliasException(descriptor.Alias);
            _contracts[descriptor.Alias] = (descriptor.ClrType, descriptor.SupportsNull);
        }
    }

    public bool TryGetSupportsNull(string alias, out bool supportsNull)
    {
        if (!_contracts.TryGetValue(alias, out var contract))
        {
            // Convention aliases and case variants resolve through the same activated CLR alias authority.
            if (!_types.TryGetTypeOrDefault(alias, out var type) ||
                !_contracts.TryGetValue(_types.GetAliasOrDefault(type), out contract))
            { supportsNull = false; return false; }
        }
        supportsNull = contract.SupportsNull;
        return true;
    }
}
