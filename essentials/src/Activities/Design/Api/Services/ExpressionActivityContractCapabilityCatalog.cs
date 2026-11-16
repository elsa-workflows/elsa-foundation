using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Expressions.Core.Contracts;

namespace Elsa.Activities.Design.Api.Services;

/// <summary>Projects the activated expression type catalog into the public Activity authoring contract.</summary>
public sealed class ExpressionActivityContractCapabilityCatalog : IActivityContractCapabilityCatalog
{
    public ExpressionActivityContractCapabilityCatalog(
        IVariableTypeDescriptorCatalog types,
        IEnumerable<IActivityContractStorageDriverProvider> storageDriverProviders)
    {
        var activatedDrivers = storageDriverProviders
            .SelectMany(x => x.DriverKeys)
            .ToHashSet(StringComparer.Ordinal);
        Types = types.GetDescriptors()
            .OrderBy(x => x.Alias, StringComparer.Ordinal)
            .Select(x => new ActivityContractTypeCapability(
                x.Alias,
                x.DisplayName,
                x.Category,
                x.DefaultEditor,
                new HashSet<Elsa.Primitives.Models.CollectionKind>(x.SupportedCollectionKinds),
                x.SupportsNull,
                x.SupportsDurability,
                x.CompatibleStorageDriverKeys.Where(activatedDrivers.Contains).ToHashSet(StringComparer.Ordinal)))
            .ToArray();
    }

    public IReadOnlyCollection<ActivityContractTypeCapability> Types { get; }
}
