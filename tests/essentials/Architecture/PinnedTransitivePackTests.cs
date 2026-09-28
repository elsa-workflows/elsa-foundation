using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;
using Xunit;
using static Elsa.Architecture.Tests.RepoPaths;

namespace Elsa.Architecture.Tests;

/// <summary>
/// A real restore and <c>dotnet pack</c>, through the real SDK and NuGet, of the premises spec 149's pinned-transitive
/// edges rest on: with <c>CentralPackageTransitivePinningEnabled</c>, pack writes into the nuspec exactly the packages
/// restore lists under <c>centralTransitiveDependencyGroups</c> in <c>obj/project.assets.json</c>, at the pinned version;
/// and the lock file lists those same packages as <c>CentralTransitive</c>, together with the pinned packages the project
/// reaches only through a <c>PrivateAssets="all"</c> reference, which pack leaves out. The maps generator reads the lock
/// file for the dependency map and leaves those out as pack does, and the calculator versions packages from the map; were
/// NuGet to write something else, the map would stop describing the nuspecs, and this fails instead.
/// </summary>
/// <remarks>
/// <c>Fixture.App</c> references <c>Fixture.Lib</c>, which references <c>Microsoft.Extensions.Options</c>, so App reaches
/// Options only through a project reference, and both reach Options' own dependencies only through it. App also references
/// <c>Microsoft.Extensions.Caching.Memory</c> privately, which alone reaches <c>Microsoft.Extensions.Caching.Abstractions</c>
/// and <c>Microsoft.Extensions.Logging.Abstractions</c>, and reaches Options' dependencies too. <c>Cronos</c> is pinned and
/// reached by neither. Every version is the one the repository pins, which a restore of the repository has already put in
/// the local package folder.
/// </remarks>
public sealed class PinnedTransitivePackTests : IDisposable
{
    private static readonly string[] Pinned =
    [
        "Cronos", "Microsoft.Extensions.Caching.Abstractions", "Microsoft.Extensions.Caching.Memory",
        "Microsoft.Extensions.DependencyInjection.Abstractions", "Microsoft.Extensions.Logging.Abstractions",
        "Microsoft.Extensions.Options", "Microsoft.Extensions.Primitives"
    ];

    /// <summary>The pinned packages App reaches only through its private reference.</summary>
    private static readonly string[] ReachedOnlyPrivately = ["Microsoft.Extensions.Caching.Abstractions", "Microsoft.Extensions.Logging.Abstractions"];

    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory(nameof(PinnedTransitivePackTests));

    public PinnedTransitivePackTests()
    {
        MsBuildFixture.CopyRootBuildFiles(root.FullName);
        var pins = XDocument.Load(Path.Join(RepoRoot, "Directory.Packages.props")).Descendants("PackageVersion")
            .Where(pin => Pinned.Contains((string?)pin.Attribute("Include")));
        Write("Directory.Packages.props", $"""
            <Project>
              <PropertyGroup>
                <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
                <CentralPackageTransitivePinningEnabled>true</CentralPackageTransitivePinningEnabled>
                <RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
              </PropertyGroup>
              <ItemGroup>
                {string.Concat(pins.Select(pin => pin.ToString(SaveOptions.DisableFormatting)))}
              </ItemGroup>
            </Project>
            """);
        Write("src/Lib/Fixture.Lib.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup><PackageReference Include="Microsoft.Extensions.Options" /></ItemGroup>
            </Project>
            """);
        Write("src/Lib/Lib.cs", "namespace Fixture; public static class Lib;");
        Write("src/App/Fixture.App.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <ProjectReference Include="..\Lib\Fixture.Lib.csproj" />
                <PackageReference Include="Microsoft.Extensions.Caching.Memory" PrivateAssets="all" />
              </ItemGroup>
            </Project>
            """);
        Write("src/App/App.cs", "namespace Fixture; public static class App;");
    }

    [Fact]
    public void Pack_writes_exactly_the_pinned_transitive_packages_restore_lists_and_the_lock_file_adds_only_the_privately_reached()
    {
        var (exitCode, output) = ChildProcess.Dotnet(
            ["pack", "src/App/Fixture.App.csproj", "-c", "Release", "-o", Path.Join(root.FullName, "out"), "-nodeReuse:false"], root.FullName, TimeSpan.FromMinutes(5));
        Assert.True(exitCode == 0, $"dotnet pack failed with exit {exitCode}:\n{output}");

        var listed = NuspecDependencies(Path.Join(root.FullName, "out", "Fixture.App.4.0.0-dev.nupkg")).Where(dependency => dependency.Id != "Fixture.Lib");
        var restored = Pins(Path.Join(root.FullName, "src", "App", "obj", "project.assets.json"), "centralTransitiveDependencyGroups", "version", _ => true);
        var locked = Pins(Path.Join(root.FullName, "src", "App", "packages.lock.json"), "dependencies", "requested",
            package => package.GetProperty("type").GetString() == "CentralTransitive");

        Assert.Equal(restored, listed.Order());
        Assert.Contains(restored, pin => pin.Id == "Microsoft.Extensions.Options");
        Assert.Contains(restored, pin => pin.Id == "Microsoft.Extensions.Primitives");
        Assert.DoesNotContain(restored, pin => pin.Id == "Cronos");

        Assert.Empty(restored.Except(locked));
        Assert.Equal(ReachedOnlyPrivately, locked.Except(restored).Select(pin => pin.Id));
    }

    public void Dispose() => root.Delete(recursive: true);

    /// <summary>
    /// Each package a restore file lists under <paramref name="groups"/>, across its target frameworks but no
    /// runtime-specific target, with the version its <paramref name="range"/> starts at: what pack reads from the assets
    /// file, and what the maps generator reads from the lock file.
    /// </summary>
    private static (string Id, string Version)[] Pins(string file, string groups, string range, Func<JsonElement, bool> listed)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(file));
        return document.RootElement.GetProperty(groups).EnumerateObject()
            .Where(group => !group.Name.Contains('/', StringComparison.Ordinal))
            .SelectMany(group => group.Value.EnumerateObject())
            .Where(package => listed(package.Value))
            .Select(package => (Id: package.Name, Range: package.Value.GetProperty(range).GetString()!))
            .Select(package => (package.Id, Version: package.Range.StartsWith('[') && package.Range.EndsWith(", )", StringComparison.Ordinal) ? package.Range[1..^3] : package.Range))
            .Order()
            .ToArray();
    }

    private static IEnumerable<(string Id, string Version)> NuspecDependencies(string package)
    {
        Assert.True(File.Exists(package), $"{package} was not produced.");
        using var archive = ZipFile.OpenRead(package);
        using var stream = archive.Entries.Single(entry => entry.FullName == entry.Name && entry.Name.EndsWith(".nuspec", StringComparison.Ordinal)).Open();
        var nuspec = XDocument.Load(stream).Root!;
        return nuspec.Descendants(nuspec.Name.Namespace + "dependency")
            .Select(dependency => ((string)dependency.Attribute("id")!, (string)dependency.Attribute("version")!))
            .ToArray();
    }

    private void Write(string path, string content)
    {
        var file = Path.Join(root.FullName, path);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, content);
    }
}
