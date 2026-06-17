using CShells.Features;
using Elsa.Modularity.Core.Contracts;
using Elsa.Modularity.Core.Models;

namespace Elsa.Modularity.Nuplane.Services;

public sealed class RuntimeFeatureCatalogContributor(IRuntimeFeatureCatalogAccessor runtimeFeatureCatalog) : IFeatureCatalogContributor
{
    public async Task ContributeAsync(FeatureCatalogContributionContext context, CancellationToken cancellationToken = default)
    {
        var snapshot = await runtimeFeatureCatalog.RefreshAsync(cancellationToken);
        foreach (var descriptor in snapshot.FeatureDescriptors)
        {
            var featureName = descriptor.Id;

            if (string.IsNullOrWhiteSpace(featureName))
                continue;

            var builder = context.GetOrAdd(featureName);
            if (builder.SourceKind is not FeatureSourceKinds.Manifest)
                builder.SourceKind = builder.SourceKind is FeatureSourceKinds.Shell ? FeatureSourceKinds.Runtime : builder.SourceKind;

            builder.DisplayName = GetMetadataString(descriptor, "DisplayName") ?? builder.DisplayName ?? featureName;
            builder.Description = GetMetadataString(descriptor, "Description") ?? builder.Description;
        }
    }

    private static string? GetMetadataString(ShellFeatureDescriptor descriptor, string key)
    {
        return descriptor.Metadata.TryGetValue(key, out var value) ? value?.ToString() : null;
    }
}
