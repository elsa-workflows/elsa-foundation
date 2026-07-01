using CShells.Features;
using Elsa.Modularity.Core.Contracts;
using Elsa.Modularity.Core.Models;

namespace Elsa.Modularity.Nuplane.Services;

public sealed class RuntimeFeatureCatalogContributor(IRuntimeFeatureCatalog runtimeFeatureCatalog) : IFeatureCatalogContributor
{
    public async Task ContributeAsync(FeatureCatalogContributionContext context, CancellationToken cancellationToken = default)
    {
        var snapshot = await runtimeFeatureCatalog.GetSnapshotAsync(cancellationToken);
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

            // Surface the feature's resolved dependency graph (from [ShellFeature(DependsOn = ...)]) so the UI can
            // cascade enable/disable across dependencies and dependants. The manifest contributor runs after this one
            // but leaves Dependencies untouched, so the runtime-resolved list wins for loaded features.
            if (descriptor.Dependencies.Count > 0)
                builder.Dependencies = descriptor.Dependencies;

            // Surface categories/settings declared via the source-only manifest hint attributes. The package-manifest
            // contributor runs after this one and overwrites with FeatureSourceKinds.Manifest, so manifest data wins
            // for package-backed features; bundled shell/runtime features keep these reflected values.
            if (builder.SourceKind is not FeatureSourceKinds.Manifest && descriptor.StartupType is { } featureType)
            {
                var hints = ManifestHintReader.Read(featureType);
                if (builder.Categories.Count == 0 && hints.Categories.Count > 0)
                    builder.Categories = hints.Categories;
                if (builder.Settings.Count == 0 && hints.Settings.Count > 0)
                    builder.Settings = hints.Settings;
            }
        }
    }

    private static string? GetMetadataString(ShellFeatureDescriptor descriptor, string key)
    {
        return descriptor.Metadata.TryGetValue(key, out var value) ? value?.ToString() : null;
    }
}
