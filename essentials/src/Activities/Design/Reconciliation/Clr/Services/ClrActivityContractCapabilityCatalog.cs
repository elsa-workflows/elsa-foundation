using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Expressions.Core.Contracts;

namespace Elsa.Activities.Design.Reconciliation.Clr.Services;

/// <summary>The existing pure selectable-type projection for endpoint-free metadata hosts.</summary>
/// <remarks>
/// Keep byte/behavior parity with the API's default projection. TryAdd registration makes every
/// consumer use the same configured catalog and preserves a host replacement in either composition.
/// </remarks>
internal sealed class ClrActivityContractCapabilityCatalog : IActivityContractCapabilityCatalog
{
    public ClrActivityContractCapabilityCatalog(
        IVariableTypeDescriptorCatalog types,
        IEnumerable<IActivityContractStorageDriverProvider> storageDriverProviders)
    {
        var activatedDrivers = storageDriverProviders.SelectMany(x => x.DriverKeys).ToHashSet(StringComparer.Ordinal);
        Types = types.GetDescriptors()
            .OrderBy(x => x.Alias, StringComparer.Ordinal)
            .Select(x => new ActivityContractTypeCapability(
                x.Alias, x.DisplayName, x.Category, x.DefaultEditor,
                new HashSet<Elsa.Primitives.Models.CollectionKind>(x.SupportedCollectionKinds),
                x.SupportsNull, x.SupportsDurability,
                x.CompatibleStorageDriverKeys.Where(activatedDrivers.Contains).ToHashSet(StringComparer.Ordinal)))
            .ToArray();
    }

    public IReadOnlyCollection<ActivityContractTypeCapability> Types { get; }
}
