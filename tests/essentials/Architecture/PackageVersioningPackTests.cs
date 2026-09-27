using System.IO.Compression;
using System.Xml.Linq;
using Xunit;
using static Elsa.Architecture.Tests.RepoPaths;

namespace Elsa.Architecture.Tests;

/// <summary>
/// Real <c>dotnet build</c> and <c>dotnet pack</c> runs, through the real SDK and NuGet, of a two-project fixture laid
/// out like this repository, under <c>src/</c> beside a copy of the root build files: proof that a packed nupkg carries
/// what spec 150 asks of it (#2080). With the calculator's pack-properties file, passed the way a pipeline passes it,
/// its range on its own project reference is bounded below the next major (FR-007), a third party's range is left
/// exactly as central package management restored it, and the package carries the computed version, the input
/// fingerprint and the source commit (FR-018). Without that file - a dev pack, or a pack with only a global
/// <c>/p:Version</c>, which is what <c>packages.yml</c> runs today - every range and the package's contents match
/// what packing has always produced: no bounding, no fingerprint. Each way a pack could silently carry something else
/// fails it instead, leaving no package behind.
/// </summary>
/// <remarks>
/// <c>Fixture.App</c> references <c>Fixture.Lib</c> as a project and <c>Microsoft.Extensions.Primitives</c> as a package,
/// at the version the repository pins, which a restore of the repository has already put in the local package folder.
/// The fixture is a git repository of its own, since the source commit comes from git.
/// </remarks>
public sealed class PackageVersioningPackTests : IDisposable
{
    private static readonly TimeSpan PackTimeout = TimeSpan.FromMinutes(5);

    private static readonly string PrimitivesVersion = XDocument.Load(Path.Join(RepoRoot, "Directory.Packages.props"))
        .Descendants("PackageVersion").Single(item => (string?)item.Attribute("Include") == "Microsoft.Extensions.Primitives")
        .Attribute("Version")!.Value;

    private static readonly string AppFingerprint = "sha256:" + new string('a', 64);

    private readonly DirectoryInfo root = Directory.CreateTempSubdirectory(nameof(PackageVersioningPackTests));
    private readonly string head;

    public PackageVersioningPackTests()
    {
        MsBuildFixture.CopyRootBuildFiles(root.FullName);
        WriteCentralPackages(PrimitivesVersion);
        Write("src/Lib/Fixture.Lib.csproj", """<Project Sdk="Microsoft.NET.Sdk" />""");
        Write("src/Lib/Lib.cs", "namespace Fixture; public static class Lib;");
        Write("src/App/Fixture.App.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <ProjectReference Include="..\Lib\Fixture.Lib.csproj" />
                <PackageReference Include="Microsoft.Extensions.Primitives" />
              </ItemGroup>
            </Project>
            """);
        Write("src/App/App.cs", "namespace Fixture; public static class App;");

        Git("init", "--quiet", "--initial-branch=main");
        Git("add", "--all");
        Git("commit", "--quiet", "-m", "fixture");
        head = Git("rev-parse", "HEAD");
    }

    private string Output => Path.Join(root.FullName, "out");

    /// <summary>
    /// Without computed input, packing is unaffected by spec 150: no range is bounded, whatever it is a range on, and
    /// no fingerprint travels in the package - the same shape a dev pack, and a pack with only a global
    /// <c>/p:Version</c>, has always had.
    /// </summary>
    [Fact]
    public void A_dev_pack_leaves_every_range_unbounded_and_carries_no_fingerprint()
    {
        Dotnet("pack", "src/App/Fixture.App.csproj", "-c", "Release", "-o", Output);

        var package = Path.Join(Output, "Fixture.App.4.0.0-dev.nupkg");
        Assert.Equal([("Fixture.Lib", "4.0.0-dev"), ("Microsoft.Extensions.Primitives", PrimitivesVersion)], Dependencies(package));
        Assert.Null(Entry(package, "elsa-input-fingerprint.json"));
    }

    /// <summary>
    /// #2080's own gate: <c>packages.yml</c> still packs with a global <c>/p:Version</c> and no calculator input
    /// (until #2082 rewrites it), so that path must go on producing exactly what it always has - no bounded range, no
    /// fingerprint - or the next merge to main would change what today's workflow publishes.
    /// </summary>
    [Fact]
    public void A_pack_with_only_a_global_version_matches_todays_unbounded_ranges_and_carries_no_fingerprint()
    {
        Dotnet("pack", "src/App/Fixture.App.csproj", "-c", "Release", "-p:Version=4.0.0-preview.999", "-o", Output);

        var package = Path.Join(Output, "Fixture.App.4.0.0-preview.999.nupkg");
        Assert.Equal(
            [("Fixture.Lib", "4.0.0-preview.999"), ("Microsoft.Extensions.Primitives", PrimitivesVersion)],
            Dependencies(package));
        Assert.Null(Entry(package, "elsa-input-fingerprint.json"));
    }

    /// <summary>
    /// The pipeline's shape: build, then pack without building, both with the same pack-properties file. The package
    /// takes its computed version; its range on an unpublished reference starts at that reference's recorded version.
    /// A third party's range is left exactly as central package management restored it (spec 150 Decisions, FR-007) -
    /// here, the plain version Directory.Packages.props names, with no upper bound.
    /// </summary>
    [Fact]
    public void A_computed_pack_carries_its_version_fingerprint_and_source_commit()
    {
        var properties = Computed(head);

        Dotnet("build", "src/App/Fixture.App.csproj", "-c", "Release", $"-p:CustomBeforeDirectoryBuildProps={properties}");
        Dotnet("pack", "src/App/Fixture.App.csproj", "-c", "Release", "--no-build", $"-p:CustomBeforeDirectoryBuildProps={properties}", "-o", Output);

        var package = Path.Join(Output, "Fixture.App.4.0.8-preview.nupkg");
        var metadata = Metadata(package);
        Assert.Equal("4.0.8-preview", metadata.Element(metadata.Name.Namespace + "version")!.Value);
        Assert.Equal(head, (string?)metadata.Element(metadata.Name.Namespace + "repository")!.Attribute("commit"));
        Assert.Equal([("Fixture.Lib", "[4.0.3-preview, 5.0.0)"), ("Microsoft.Extensions.Primitives", PrimitivesVersion)], Dependencies(package));
        Assert.Equal($$"""{"schema_version":1,"fingerprint":"{{AppFingerprint}}"}""", Entry(package, "elsa-input-fingerprint.json")?.Trim());
    }

    /// <summary>A no-build pack stamping another version than the build did would ship assemblies claiming a different one.</summary>
    [Fact]
    public void A_no_build_pack_with_other_version_input_than_its_build_fails()
    {
        Dotnet("build", "src/App/Fixture.App.csproj", "-c", "Release");

        var output = DotnetFailing(
            "pack", "src/App/Fixture.App.csproj", "-c", "Release", "--no-build", $"-p:CustomBeforeDirectoryBuildProps={Computed(head)}", "-o", Output);

        Assert.Contains("ELSAPV007", output);
        Assert.False(Directory.Exists(Output) && Directory.EnumerateFiles(Output, "*.nupkg").Any());
    }

    /// <summary>Versions and fingerprints computed for another commit describe inputs other than the ones packed.</summary>
    [Fact]
    public void A_computation_for_another_commit_fails_the_pack()
    {
        var output = DotnetFailing(
            "pack", "src/App/Fixture.App.csproj", "-c", "Release", $"-p:CustomBeforeDirectoryBuildProps={Computed(new string('1', 40))}", "-o", Output);

        Assert.Contains("ELSAPV006", output);
        Assert.False(Directory.Exists(Output) && Directory.EnumerateFiles(Output, "*.nupkg").Any());
    }

    /// <summary>
    /// A third party's range is never checked here, however it is shaped: only this repository's own ranges are
    /// (spec 150 Decisions, FR-007). <see cref="PackageRangesBuildCheckTests"/> proves ElsaVerifyNuspecRanges rejects
    /// an unbounded range on one of this repository's own packages the same way; a real pack cannot manufacture that
    /// case, because every other check here refuses a version that is not what the computation - or the dev fallback -
    /// says it should be, project references included.
    /// </summary>
    [Fact]
    public void A_third_partys_range_left_unbounded_by_hand_still_passes_the_pack()
    {
        WriteCentralPackages($"[{PrimitivesVersion}, )");
        var properties = Computed(head);

        Dotnet("build", "src/App/Fixture.App.csproj", "-c", "Release", $"-p:CustomBeforeDirectoryBuildProps={properties}");
        Dotnet("pack", "src/App/Fixture.App.csproj", "-c", "Release", "--no-build", $"-p:CustomBeforeDirectoryBuildProps={properties}", "-o", Output);

        var package = Path.Join(Output, "Fixture.App.4.0.8-preview.nupkg");
        Assert.Equal([("Fixture.Lib", "[4.0.3-preview, 5.0.0)"), ("Microsoft.Extensions.Primitives", PrimitivesVersion)], Dependencies(package));
    }

    public void Dispose() => root.Delete(recursive: true);

    /// <summary>The calculator's pack-properties file for <paramref name="commit"/>: App is being published, Lib is not.</summary>
    private string Computed(string commit)
    {
        var path = Path.Join(root.FullName, $"package-versions-{commit}.props");
        File.WriteAllText(path, $"""
            <Project>
              <PropertyGroup>
                <ElsaVersionComputationCommit>{commit}</ElsaVersionComputationCommit>
              </PropertyGroup>
              <PropertyGroup Condition="'$(MSBuildProjectName)' == 'Fixture.App'">
                <ElsaComputedPackageVersion>4.0.8-preview</ElsaComputedPackageVersion>
                <ElsaInputFingerprint>{AppFingerprint}</ElsaInputFingerprint>
              </PropertyGroup>
              <PropertyGroup Condition="'$(MSBuildProjectName)' == 'Fixture.Lib'">
                <ElsaComputedPackageVersion>4.0.3-preview</ElsaComputedPackageVersion>
                <ElsaInputFingerprint>sha256:{new string('b', 64)}</ElsaInputFingerprint>
              </PropertyGroup>
            </Project>
            """);
        return path;
    }

    private void WriteCentralPackages(string primitivesVersion) => Write("Directory.Packages.props", $"""
        <Project>
          <PropertyGroup>
            <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
          </PropertyGroup>
          <ItemGroup>
            <PackageVersion Include="Microsoft.Extensions.Primitives" Version="{primitivesVersion}" />
          </ItemGroup>
        </Project>
        """);

    private void Write(string path, string content)
    {
        var file = Path.Join(root.FullName, path);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, content);
    }

    private string Git(params string[] arguments)
    {
        // Isolated from the machine's git configuration: no signing, no hooks, a fixed identity.
        var (exitCode, output) = ChildProcess.Run("git",
            ["-C", root.FullName, "-c", "commit.gpgsign=false", "-c", "core.hooksPath=.git/no-hooks", "-c", "user.name=Pack Fixture",
             "-c", "user.email=pack-fixture@example.invalid", .. arguments]);
        Assert.True(exitCode == 0, $"git {string.Join(' ', arguments)} failed:\n{output}");
        return output.Trim();
    }

    private void Dotnet(params string[] arguments)
    {
        var (exitCode, output) = ChildProcess.Dotnet([.. arguments, "-nodeReuse:false"], root.FullName, PackTimeout);
        Assert.True(exitCode == 0, $"dotnet {string.Join(' ', arguments)} failed with exit {exitCode}:\n{output}");
    }

    private string DotnetFailing(params string[] arguments)
    {
        var (exitCode, output) = ChildProcess.Dotnet([.. arguments, "-nodeReuse:false"], root.FullName, PackTimeout);
        Assert.True(exitCode != 0, $"expected dotnet {string.Join(' ', arguments)} to fail, but it succeeded:\n{output}");
        return output;
    }

    /// <summary>The nuspec's <c>metadata</c> element: a package holds one nuspec, at its root.</summary>
    private static XElement Metadata(string package)
    {
        Assert.True(File.Exists(package), $"{package} was not produced.");
        using var archive = ZipFile.OpenRead(package);
        using var stream = archive.Entries.Single(entry => entry.FullName == entry.Name && entry.Name.EndsWith(".nuspec", StringComparison.Ordinal)).Open();
        var nuspec = XDocument.Load(stream).Root!;
        return nuspec.Element(nuspec.Name.Namespace + "metadata")!;
    }

    private static (string Id, string Range)[] Dependencies(string package)
    {
        var metadata = Metadata(package);
        return metadata.Descendants(metadata.Name.Namespace + "dependency")
            .Select(dependency => ((string)dependency.Attribute("id")!, (string)dependency.Attribute("version")!))
            .ToArray();
    }

    /// <summary>The text of an entry in a package, or null when it has none.</summary>
    private static string? Entry(string package, string entryName)
    {
        Assert.True(File.Exists(package), $"{package} was not produced.");
        using var archive = ZipFile.OpenRead(package);
        using var reader = archive.GetEntry(entryName) is { } entry ? new StreamReader(entry.Open()) : null;
        return reader?.ReadToEnd();
    }
}
