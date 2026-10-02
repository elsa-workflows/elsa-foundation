using System.Reflection;
using CShells.Features;

namespace Elsa.Studio.Tests;

/// <summary>
/// Returns a snapshot whose feature descriptors carry the <see cref="ShellFeatureDescriptor.StartupType"/>
/// of each supplied type so that <see cref="Elsa.Studio.Api.Services.StudioModuleManifestProvider"/>
/// can reflect <c>[StudioModule]</c> attributes from them.
/// </summary>
internal sealed class FakeRuntimeFeatureCatalog : IRuntimeFeatureCatalog
{
    private readonly RuntimeFeatureCatalogSnapshot _snapshot;

    public FakeRuntimeFeatureCatalog(params Type[] featureTypes)
    {
        var descriptors = featureTypes
            .Select(type =>
            {
                // Extract the feature name from the [ShellFeature] attribute if present, otherwise use the type name.
                var shellFeatureAttr = type.GetCustomAttributesData()
                    .FirstOrDefault(a => a.AttributeType.Name == "ShellFeatureAttribute");

                var featureName = shellFeatureAttr?.ConstructorArguments.FirstOrDefault().Value as string ?? type.Name;

                return new ShellFeatureDescriptor { Id = featureName, StartupType = type };
            })
            .ToArray();

        _snapshot = new RuntimeFeatureCatalogSnapshot(
            1,
            Array.Empty<Assembly>(),
            descriptors,
            new Dictionary<string, ShellFeatureDescriptor>(),
            DateTimeOffset.UtcNow);
    }

    public Task<RuntimeFeatureCatalogSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_snapshot);

    public Task<RuntimeFeatureCatalogSnapshot> RefreshAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_snapshot);
}
