using System.Reflection;
using Elsa.Modularity.Core.Contracts;
using Elsa.Modularity.Core.Models;

namespace Elsa.Modularity.Nuplane.Services;

public sealed class RuntimeFeatureCatalogContributor(IServiceProvider serviceProvider) : IFeatureCatalogContributor
{
    public async Task ContributeAsync(FeatureCatalogContributionContext context, CancellationToken cancellationToken = default)
    {
        var snapshot = await RuntimeFeatureCatalogReflection.RefreshAsync(serviceProvider, cancellationToken);
        foreach (var descriptor in RuntimeFeatureCatalogReflection.EnumerateFeatureDescriptors(snapshot))
        {
            var featureName = GetStringProperty(descriptor, "FeatureName")
                ?? GetStringProperty(descriptor, "Name")
                ?? GetStringProperty(descriptor, "Id");

            if (string.IsNullOrWhiteSpace(featureName))
                continue;

            var builder = context.GetOrAdd(featureName);
            if (builder.SourceKind is not FeatureSourceKinds.Manifest)
                builder.SourceKind = builder.SourceKind is FeatureSourceKinds.Shell ? FeatureSourceKinds.Runtime : builder.SourceKind;

            builder.DisplayName = GetStringProperty(descriptor, "DisplayName") ?? builder.DisplayName ?? featureName;
            builder.Description = GetStringProperty(descriptor, "Description") ?? builder.Description;
        }
    }

    private static string? GetStringProperty(object source, string propertyName)
    {
        var value = source.GetType().GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance)?.GetValue(source);
        return value?.ToString();
    }
}
