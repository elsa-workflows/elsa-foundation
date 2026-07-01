using System.Text.Json;
using Elsa.Modularity.Core.Models;
using Elsa.Modularity.Nuplane.Services;
using Xunit;

namespace Elsa.Modularity.Tests;

public sealed class PackageManifestFeatureCatalogContributorTests : IAsyncDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"elsa-package-manifest-{Guid.NewGuid():N}");

    public PackageManifestFeatureCatalogContributorTests()
    {
        Directory.CreateDirectory(_directory);
    }

    [Fact]
    public void ReadManifestParsesFeatureSettingMetadata()
    {
        File.WriteAllText(Path.Combine(_directory, "elsa-package.json"), """
        {
          "package": {
            "id": "Elsa.FeaturePackage",
            "version": "1.2.3"
          },
          "features": [
            {
              "id": "ManifestFeature",
              "displayName": "Manifest Feature",
              "category": "Runtime",
              "categories": ["Advanced"],
              "settings": [
                {
                  "name": "Mode",
                  "clrType": "System.String",
                  "jsonType": "string",
                  "required": true,
                  "defaultValue": "auto",
                  "displayName": "Mode",
                  "description": "Execution mode.",
                  "category": "General",
                  "secret": false,
                  "restartRequired": true,
                  "validation": {
                    "enum": ["auto", "manual"]
                  },
                  "ui": {
                    "hint": "select",
                    "group": "Runtime",
                    "advanced": true,
                    "experimental": false
                  },
                  "extensions": {
                    "sensitive": true
                  }
                }
              ],
              "extensions": {
                "cshellsFeatureName": "RuntimeManifestFeature"
              },
              "dependencies": [
                { "featureId": "Elsa.FeaturePackage.OtherFeature" }
              ]
            }
          ]
        }
        """);

        var result = PackageManifestFeatureCatalogContributor.ReadManifest(_directory);

        Assert.NotNull(result.Manifest);
        var feature = Assert.Single(result.Manifest.Features);
        Assert.Equal("ManifestFeature", feature.Id);
        Assert.Equal("RuntimeManifestFeature", ((JsonElement)feature.Extensions["cshellsFeatureName"]!).GetString());
        var setting = Assert.Single(feature.Settings);
        Assert.Equal("Mode", setting.Name);
        Assert.Equal("string", setting.JsonType);
        Assert.True(setting.Required);
        Assert.True(setting.RestartRequired);
        Assert.True(((JsonElement)setting.Extensions["sensitive"]!).GetBoolean());
        Assert.Equal("select", ((JsonElement)setting.UI["hint"]!).GetString());
        Assert.Equal("Runtime", ((JsonElement)setting.UI["group"]!).GetString());
        Assert.Equal("auto", ((JsonElement)setting.DefaultValue!).GetString());
        Assert.Collection(
            ((JsonElement)setting.Validation["enum"]!).EnumerateArray(),
            value => Assert.Equal("auto", value.GetString()),
            value => Assert.Equal("manual", value.GetString()));
        var dependency = Assert.Single(feature.Dependencies);
        Assert.Equal("Elsa.FeaturePackage.OtherFeature", dependency.FeatureId);
    }

    [Fact]
    public void ReadManifestReportsMissingManifest()
    {
        var result = PackageManifestFeatureCatalogContributor.ReadManifest(_directory);

        Assert.Null(result.Manifest);
        Assert.Equal("Package manifest not found.", result.ReadError);
    }

    [Fact]
    public void ApplyStripsSamePackagePrefixFromDependencyFeatureIds()
    {
        File.WriteAllText(Path.Combine(_directory, "elsa-package.json"), """
        {
          "package": { "id": "Elsa.FeaturePackage", "version": "1.0.0" },
          "features": [
            {
              "id": "ManifestFeature",
              "dependencies": [
                { "featureId": "Elsa.FeaturePackage.OtherFeature" },
                { "featureId": "External.Feature" }
              ]
            }
          ]
        }
        """);

        var result = PackageManifestFeatureCatalogContributor.ReadManifest(_directory);
        var context = CreateContext();

        PackageManifestCatalogMapper.Apply(context, result.Manifest!, result.Path, result.Hash, "Elsa.FeaturePackage", "1.0.0");

        Assert.Equal(["OtherFeature", "External.Feature"], context.Items["ManifestFeature"].Dependencies);
    }

    [Fact]
    public void ApplyDoesNotOverwriteDependenciesAlreadyResolvedAtRuntime()
    {
        File.WriteAllText(Path.Combine(_directory, "elsa-package.json"), """
        {
          "package": { "id": "Elsa.FeaturePackage", "version": "1.0.0" },
          "features": [
            { "id": "ManifestFeature", "dependencies": [ { "featureId": "Elsa.FeaturePackage.StaleDependency" } ] }
          ]
        }
        """);

        var result = PackageManifestFeatureCatalogContributor.ReadManifest(_directory);
        var context = CreateContext();
        context.GetOrAdd("ManifestFeature").Dependencies = ["RuntimeResolvedDependency"];

        PackageManifestCatalogMapper.Apply(context, result.Manifest!, result.Path, result.Hash, "Elsa.FeaturePackage", "1.0.0");

        Assert.Equal(["RuntimeResolvedDependency"], context.Items["ManifestFeature"].Dependencies);
    }

    private static FeatureCatalogContributionContext CreateContext() =>
        new(new ShellFeatureConfigurationSnapshot("default", "revision", new Dictionary<string, JsonElement>()));

    public ValueTask DisposeAsync()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);

        return ValueTask.CompletedTask;
    }
}
