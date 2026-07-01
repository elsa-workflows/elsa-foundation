using System.Text;
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
    public void ReadManifestParsesManifestWithUtf8ByteOrderMark()
    {
        // A BOM-emitting editor/tool can prepend a UTF-8 BOM; System.Text.Json's byte overload rejects it, so the
        // reader must strip it rather than silently dropping every feature the package would have contributed.
        var json = """
        {
          "package": { "id": "Elsa.FeaturePackage", "version": "1.0.0" },
          "features": [ { "id": "ManifestFeature", "displayName": "Manifest Feature" } ]
        }
        """;
        File.WriteAllText(Path.Combine(_directory, "elsa-package.json"), json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        var result = PackageManifestFeatureCatalogContributor.ReadManifest(_directory);

        Assert.NotNull(result.Manifest);
        var feature = Assert.Single(result.Manifest.Features);
        Assert.Equal("ManifestFeature", feature.Id);
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

        var dependencies = context.Items["ManifestFeature"].Dependencies;
        Assert.Equal(["OtherFeature", "External.Feature"], dependencies.Select(dependency => dependency.Id));
        Assert.All(dependencies, dependency => Assert.False(dependency.Optional));
    }

    [Fact]
    public void ApplyThreadsOptionalDependencyFlag()
    {
        File.WriteAllText(Path.Combine(_directory, "elsa-package.json"), """
        {
          "package": { "id": "Elsa.FeaturePackage", "version": "1.0.0" },
          "features": [
            {
              "id": "ManifestFeature",
              "dependencies": [
                { "featureId": "Elsa.FeaturePackage.RequiredFeature" },
                { "featureId": "Elsa.FeaturePackage.OptionalFeature", "optional": true }
              ]
            }
          ]
        }
        """);

        var result = PackageManifestFeatureCatalogContributor.ReadManifest(_directory);
        var context = CreateContext();

        PackageManifestCatalogMapper.Apply(context, result.Manifest!, result.Path, result.Hash, "Elsa.FeaturePackage", "1.0.0");

        var dependencies = context.Items["ManifestFeature"].Dependencies;
        Assert.Equal(["RequiredFeature", "OptionalFeature"], dependencies.Select(dependency => dependency.Id));
        Assert.Equal([false, true], dependencies.Select(dependency => dependency.Optional));
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
        var builder = context.GetOrAdd("ManifestFeature");
        builder.Dependencies = [new FeatureDependency("RuntimeResolvedDependency", Optional: false)];
        builder.DependenciesResolved = true;

        PackageManifestCatalogMapper.Apply(context, result.Manifest!, result.Path, result.Hash, "Elsa.FeaturePackage", "1.0.0");

        Assert.Equal(["RuntimeResolvedDependency"], context.Items["ManifestFeature"].Dependencies.Select(dependency => dependency.Id));
    }

    [Fact]
    public void ApplyDoesNotClobberDependenciesAlreadyFilledByAnEarlierManifest()
    {
        // Two package manifests declare the same feature and neither is loaded at runtime (DependenciesResolved stays
        // false). The first manifest to fill the dependency list must win; a later manifest must not overwrite it.
        var context = CreateContext();

        File.WriteAllText(Path.Combine(_directory, "elsa-package.json"), """
        {
          "package": { "id": "Elsa.FeaturePackage", "version": "1.0.0" },
          "features": [
            { "id": "ManifestFeature", "dependencies": [ { "featureId": "Elsa.FeaturePackage.FirstDependency" } ] }
          ]
        }
        """);
        var first = PackageManifestFeatureCatalogContributor.ReadManifest(_directory);
        PackageManifestCatalogMapper.Apply(context, first.Manifest!, first.Path, first.Hash, "Elsa.FeaturePackage", "1.0.0");

        File.WriteAllText(Path.Combine(_directory, "elsa-package.json"), """
        {
          "package": { "id": "Other.FeaturePackage", "version": "2.0.0" },
          "features": [
            { "id": "ManifestFeature", "dependencies": [ { "featureId": "Other.FeaturePackage.SecondDependency" } ] }
          ]
        }
        """);
        var second = PackageManifestFeatureCatalogContributor.ReadManifest(_directory);
        PackageManifestCatalogMapper.Apply(context, second.Manifest!, second.Path, second.Hash, "Other.FeaturePackage", "2.0.0");

        Assert.Equal(["FirstDependency"], context.Items["ManifestFeature"].Dependencies.Select(dependency => dependency.Id));
    }

    [Fact]
    public void ApplyDoesNotFillDependenciesWhenRuntimeResolvedZero()
    {
        // A loaded feature whose runtime descriptor resolved zero dependencies must not be backfilled from a stale
        // manifest that still lists some — the runtime "was resolved" signal wins over list emptiness.
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
        var builder = context.GetOrAdd("ManifestFeature");
        builder.DependenciesResolved = true;

        PackageManifestCatalogMapper.Apply(context, result.Manifest!, result.Path, result.Hash, "Elsa.FeaturePackage", "1.0.0");

        Assert.Empty(context.Items["ManifestFeature"].Dependencies);
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
