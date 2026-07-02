using System.Text.Json;
using Elsa.Modularity.Core.Models;
using Elsa.Server;
using Nuplane.Abstractions;
using Xunit;

namespace Elsa.Modularity.Tests;

public sealed class ModuleManagementRegistryBuilderTests : IAsyncDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"elsa-module-registry-{Guid.NewGuid():N}");

    public ModuleManagementRegistryBuilderTests()
    {
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public void BuildModulesGroupsPackageFeaturesUnderActivePackage()
    {
        var installPath = CreatePackageDirectory("Elsa.PackageA", "1.0.0");
        var feature = NewFeature("FeatureA", "Feature A", "Elsa.PackageA", "1.0.0");

        var modules = ModuleManagementRegistryBuilder.BuildModules([feature], [NewPackage("Elsa.PackageA", "1.0.0", installPath)]);

        var module = Assert.Single(modules);
        Assert.Equal("Elsa.PackageA@1.0.0", module.Id);
        Assert.Equal("Elsa.PackageA", module.PackageId);
        Assert.Equal("1.0.0", module.Version);
        Assert.Equal("loaded", module.Status);
        Assert.Equal("compatible", module.Compatibility);
        var child = Assert.Single(module.Features);
        Assert.Equal("FeatureA", child.Id);
        Assert.Equal("Feature A", child.DisplayName);
    }

    [Fact]
    public void BuildModulesGroupsUnpackagedFeaturesUnderServerModule()
    {
        var feature = NewFeature("RuntimeFeature", "Runtime Feature", null, null, FeatureSourceKinds.Runtime);

        var modules = ModuleManagementRegistryBuilder.BuildModules([feature], []);

        var module = Assert.Single(modules);
        Assert.Equal("Elsa.Server", module.Id);
        Assert.Equal("server", module.SourceKind);
        Assert.Equal("loaded", module.Status);
        Assert.Equal("RuntimeFeature", Assert.Single(module.Features).Id);
    }

    [Fact]
    public void BuildModulesReportsManifestErrorsOnPackageNotAsChildFeature()
    {
        var installPath = Path.Combine(_directory, "missing-manifest");
        Directory.CreateDirectory(installPath);
        var error = NewFeature(
            "Elsa.Missing:1.0.0",
            "Elsa.Missing",
            "Elsa.Missing",
            "1.0.0",
            FeatureSourceKinds.ManifestError,
            readError: "Package manifest not found.");

        var module = Assert.Single(ModuleManagementRegistryBuilder.BuildModules([error], [NewPackage("Elsa.Missing", "1.0.0", installPath)]));

        Assert.Empty(module.Features);
        Assert.Equal("failed", module.Status);
        Assert.Contains(module.Diagnostics, x => x.Source == "Elsa.Missing:1.0.0" && x.Status == "failed");
    }

    [Fact]
    public void BuildModulesWarnsWhenManifestIdentityDiffersFromActivePackage()
    {
        var installPath = CreatePackageDirectory("Elsa.ManifestIdentity", "2.0.0");
        var feature = NewFeature("FeatureA", "Feature A", "Elsa.ActiveIdentity", "1.0.0");

        var module = Assert.Single(ModuleManagementRegistryBuilder.BuildModules([feature], [NewPackage("Elsa.ActiveIdentity", "1.0.0", installPath)]));

        Assert.Equal("warning", module.Compatibility);
        Assert.Contains(module.Diagnostics, x => x.Source == "manifest" && x.Status == "warning" && x.Reason.Contains("differs", StringComparison.OrdinalIgnoreCase));
        Assert.Equal("FeatureA", Assert.Single(module.Features).Id);
    }

    public ValueTask DisposeAsync()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);

        return ValueTask.CompletedTask;
    }

    private string CreatePackageDirectory(string packageId, string version)
    {
        var path = Path.Combine(_directory, $"{packageId}-{version}");
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "elsa-package.json"), $$"""
        {
          "package": {
            "id": "{{packageId}}",
            "version": "{{version}}"
          },
          "features": []
        }
        """);
        return path;
    }

    private static ActivePackage NewPackage(string packageId, string version, string installPath) =>
        new(packageId, version, "local", "drop", installPath, DateTimeOffset.UtcNow, "corr", "graph", "gen", default, [], [], true);

    private static FeatureCatalogItem NewFeature(
        string id,
        string displayName,
        string? packageId,
        string? packageVersion,
        string sourceKind = FeatureSourceKinds.Manifest,
        string? readError = null) =>
        new(
            id,
            displayName,
            null,
            [],
            sourceKind,
            packageId,
            packageVersion,
            sourceKind == FeatureSourceKinds.Manifest,
            Json("{}"),
            false,
            false,
            packageId is null ? null : "elsa-package.json",
            packageId is null ? null : "hash",
            readError,
            [],
            []);

    private static JsonElement Json(string json) =>
        JsonDocument.Parse(json).RootElement.Clone();
}
