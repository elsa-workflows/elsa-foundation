using CShells.Features;

namespace Elsa.Modularity.Nuplane.Services;

public interface IRuntimeFeatureCatalogAccessor
{
    Task<RuntimeFeatureCatalogSnapshot> RefreshAsync(CancellationToken cancellationToken = default);
}

public sealed record RuntimeFeatureCatalogSnapshot(IReadOnlyCollection<ShellFeatureDescriptor> FeatureDescriptors);
