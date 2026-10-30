using System.Text.Json;
using Elsa.Specifications.PackageManifests;

namespace Elsa.Studio.Tests;

public sealed class StudioGeneratedManifestTests
{
    [Fact]
    public async Task GeneratedApiManifestRoundTripsThroughTheCurrentContractWithStudioHints()
    {
        var json = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "WebHost", "api-elsa-package.json"));
        var manifest = JsonSerializer.Deserialize<ElsaPackageManifest>(json, ManifestJsonSerializerOptions.Default);
        Assert.NotNull(manifest);
        Assert.Equal("Elsa.Studio.Api", manifest.Package.Id);
        Assert.Single(manifest.Features, feature => feature.Id == "Elsa.Studio.Api.StudioApi");

        using var roundTrip = JsonDocument.Parse(JsonSerializer.Serialize(manifest, ManifestJsonSerializerOptions.Default));
        var feature = roundTrip.RootElement.GetProperty("features").EnumerateArray()
            .Single(value => value.GetProperty("id").GetString() == "Elsa.Studio.Api.StudioApi");
        Assert.Equal("StudioApi", feature.GetProperty("extensions").GetProperty("cshellsFeatureName").GetString());
        Assert.Contains("elsa.studio", feature.GetProperty("compatibility").GetProperty("runtimeKinds").EnumerateArray().Select(value => value.GetString()));
        Assert.Contains("API", feature.GetProperty("categories").EnumerateArray().Select(value => value.GetString()));
    }
}
