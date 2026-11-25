using System.Text.Json;
using Xunit;

namespace Elsa.Architecture.Tests;

public sealed class JsonHashExternalSourcesTests
{
    [Fact]
    public void Newer_restored_assets_make_the_older_shipping_image_stale()
    {
        var root = Directory.CreateTempSubdirectory("elsa-s9-assets-freshness-").FullName;
        try
        {
            var project = Path.Join(root, "Fixture.csproj"); File.WriteAllText(project, "<Project />");
            var asset = Path.Join(root, "obj", "project.assets.json"); Directory.CreateDirectory(Path.GetDirectoryName(asset)!); File.WriteAllText(asset, "{}");
            var image = Path.Join(root, "Fixture.dll"); File.WriteAllBytes(image, FixtureCompiler.Compile("Fixture", "public struct Payload { }"));
            using var evaluated = JsonDocument.Parse(JsonSerializer.Serialize(new
            {
                Properties = new { MSBuildAllProjects = "", ProjectAssetsFile = "obj/project.assets.json" },
                Items = new { Compile = Array.Empty<object>(), AdditionalFiles = Array.Empty<object>(), EditorConfigFiles = Array.Empty<object>(), Analyzer = Array.Empty<object>() }
            }));
            var inputs = EvaluatedHashInputs.Parse(evaluated.RootElement, project, []);
            Assert.Contains(asset, inputs);
            File.SetLastWriteTimeUtc(project, DateTime.UtcNow.AddMinutes(-3));
            File.SetLastWriteTimeUtc(asset, DateTime.UtcNow.AddMinutes(-2));
            File.SetLastWriteTimeUtc(image, DateTime.UtcNow.AddMinutes(-1));
            var entry = new ShippingHashAssemblies.Project(project, image);
            Assert.Empty(ShippingHashAssemblies.ValidateInputs(root, [entry], "Debug", _ => inputs));
            File.SetLastWriteTimeUtc(asset, DateTime.UtcNow);
            Assert.Contains(ShippingHashAssemblies.ValidateInputs(root, [entry], "Debug", _ => inputs), error => error.StartsWith("Stale shipping assembly", StringComparison.Ordinal));
        }
        finally
        {
            Assert.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar,
                Path.GetFullPath(root), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
            Directory.Delete(root, recursive: true);
        }
    }
    [Theory]
    [InlineData("../escape", "Source.cs")]
    [InlineData("example/1.2.3", "../../../Source.cs")]
    public void Restored_source_labels_cannot_escape_their_package(string package, string file)
    {
        var root = Directory.CreateTempSubdirectory("elsa-s9-package-boundary-").FullName;
        try
        {
            var assets = Path.Join(root, "assets.json");
            File.WriteAllText(assets, JsonSerializer.Serialize(new
            {
                packageFolders = new Dictionary<string, object> { [Path.Join(root, "cache")] = new { } },
                libraries = new Dictionary<string, object> { ["Example/1.2.3"] = new { type = "package", path = package, files = new[] { file } } }
            }));
            Assert.Throws<InvalidOperationException>(() => JsonHashExternalSources.Read(assets, new HashSet<string>()));
        }
        finally
        {
            Assert.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar,
                Path.GetFullPath(root), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
            Directory.Delete(root, recursive: true);
        }
    }
    [Fact]
    public void Only_exact_restored_package_compile_files_receive_portable_cache_independent_names()
    {
        var root = Directory.CreateTempSubdirectory("elsa-s9-package-source-").FullName;
        try
        {
            string Read(string cache)
            {
                var file = Path.GetFullPath(Path.Join(cache, "example", "1.2.3", "content", "Source.cs"));
                var unlisted = Path.GetFullPath(Path.Join(cache, "example", "1.2.3", "other", "Source.cs"));
                var assets = Path.Join(root, "assets.json");
                File.WriteAllText(assets, JsonSerializer.Serialize(new
                {
                    packageFolders = new Dictionary<string, object> { [cache] = new { } },
                    libraries = new Dictionary<string, object> { ["Example/1.2.3"] = new { type = "package", path = "example/1.2.3", files = new[] { "content/Source.cs" } } }
                }));
                var mapping = JsonHashExternalSources.Read(assets, new HashSet<string> { file, unlisted });
                Assert.False(mapping.ContainsKey(unlisted));
                return Assert.Single(mapping).Value;
            }
            Assert.Equal("nuget:example/1.2.3/content/Source.cs", Read(Path.Join(root, "cache-a")));
            Assert.Equal(Read(Path.Join(root, "cache-a")), Read(Path.Join(root, "cache-b")));
            Assert.Equal("nuget:example/1.2.3/content/Source.cs", Read(Path.GetPathRoot(root)!));
        }
        finally
        {
            Assert.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar,
                Path.GetFullPath(root), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
            Directory.Delete(root, recursive: true);
        }
    }
}
