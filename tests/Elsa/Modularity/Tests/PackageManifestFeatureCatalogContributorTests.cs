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
                  "group": "Runtime",
                  "secret": false,
                  "sensitive": true,
                  "restartRequired": true,
                  "validation": {
                    "enum": ["auto", "manual"]
                  },
                  "ui": {
                    "hint": "select",
                    "advanced": true,
                    "experimental": false
                  }
                }
              ],
              "extensions": {
                "cshellsFeatureName": "RuntimeManifestFeature"
              }
            }
          ]
        }
        """);

        var result = PackageManifestFeatureCatalogContributor.ReadManifest(_directory);

        Assert.NotNull(result.Manifest);
        var feature = Assert.Single(result.Manifest.Features!);
        Assert.Equal("ManifestFeature", feature.Id);
        Assert.Equal("RuntimeManifestFeature", feature.Extensions!["cshellsFeatureName"]!.GetValue<string>());
        var setting = Assert.Single(feature.Settings!);
        Assert.Equal("Mode", setting.Name);
        Assert.Equal("string", setting.JsonType);
        Assert.True(setting.Required);
        Assert.True(setting.Sensitive);
        Assert.True(setting.RestartRequired);
        Assert.Equal("select", setting.Ui!.Hint);
        Assert.Equal("auto", setting.DefaultValue!.Value.GetString());
        Assert.Collection(
            setting.Validation!.Enum!,
            value => Assert.Equal("auto", value.GetString()),
            value => Assert.Equal("manual", value.GetString()));
    }

    [Fact]
    public void ReadManifestReportsMissingManifest()
    {
        var result = PackageManifestFeatureCatalogContributor.ReadManifest(_directory);

        Assert.Null(result.Manifest);
        Assert.Equal("Package manifest not found.", result.ReadError);
    }

    public ValueTask DisposeAsync()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);

        return ValueTask.CompletedTask;
    }
}
