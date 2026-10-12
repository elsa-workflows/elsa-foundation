using Elsa.Platform.PackageManifests;
using Elsa.Platform.PackageManifests.Compatibility;
using Elsa.Studio.Web;

namespace Elsa.Studio.Tests;

public sealed class ElsaFeatureManagementApiTests
{
    [Fact]
    public void MergeDependenciesPrefersRuntimeDescriptorWhenPresent()
    {
        var manifest = new[] { new FeatureManagementDependency("StaleDependency", false) };

        var merged = ElsaFeatureManagementApi.MergeDependencies(hasDescriptor: true, descriptorDependencies: ["RuntimeDependency"], manifestDependencies: manifest);

        Assert.Equal(["RuntimeDependency"], merged.Select(dependency => dependency.Id));
        Assert.All(merged, dependency => Assert.False(dependency.Optional));
    }

    [Fact]
    public void MergeDependenciesKeepsEmptyWhenRuntimeResolvedZero()
    {
        // A loaded feature whose runtime descriptor resolved zero dependencies must not be backfilled from a stale
        // manifest that still lists some — the descriptor's presence, not the list's emptiness, is the signal.
        var manifest = new[] { new FeatureManagementDependency("StaleDependency", false) };

        var merged = ElsaFeatureManagementApi.MergeDependencies(hasDescriptor: true, descriptorDependencies: [], manifestDependencies: manifest);

        Assert.Empty(merged);
    }

    [Fact]
    public void MergeDependenciesFallsBackToManifestWhenNoDescriptor()
    {
        var manifest = new[]
        {
            new FeatureManagementDependency("RequiredDependency", false),
            new FeatureManagementDependency("OptionalDependency", true)
        };

        var merged = ElsaFeatureManagementApi.MergeDependencies(hasDescriptor: false, descriptorDependencies: null, manifestDependencies: manifest);

        Assert.Same(manifest, merged);
    }

    [Fact]
    public void GetDependenciesThreadsOptionalFlagAndStripsSamePackagePrefix()
    {
        var feature = new FeatureManifest
        {
            Id = "ManifestFeature",
            Dependencies =
            [
                new DependencyManifest { FeatureId = "Elsa.FeaturePackage.RequiredFeature" },
                new DependencyManifest { FeatureId = "Elsa.FeaturePackage.OptionalFeature", Optional = true },
                new DependencyManifest { FeatureId = "External.Feature" }
            ]
        };

        var dependencies = ElsaFeatureManagementApi.GetDependencies(feature, "Elsa.FeaturePackage");

        Assert.Equal(["RequiredFeature", "OptionalFeature", "External.Feature"], dependencies.Select(dependency => dependency.Id));
        Assert.Equal([false, true, false], dependencies.Select(dependency => dependency.Optional));
    }
}
