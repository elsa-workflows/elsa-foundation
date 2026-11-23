namespace Elsa.Modularity.Core.Models;

public sealed class FeatureCatalogContributionContext(ShellFeatureConfigurationSnapshot shell)
{
    private readonly Dictionary<string, FeatureCatalogItemBuilder> _items = new(StringComparer.OrdinalIgnoreCase);

    public ShellFeatureConfigurationSnapshot Shell { get; } = shell;

    public IReadOnlyDictionary<string, FeatureCatalogItemBuilder> Items => _items;

    public FeatureCatalogItemBuilder GetOrAdd(string id)
    {
        if (!_items.TryGetValue(id, out var builder))
        {
            builder = new FeatureCatalogItemBuilder
            {
                Id = id,
                SourceKind = FeatureSourceKinds.Shell
            };
            _items[id] = builder;
        }

        return builder;
    }
}
