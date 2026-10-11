using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json.Nodes;

namespace Elsa.Cluster.EntityFrameworkCore.Tests;

/// <summary>
/// The feeds the host loads from: the fixture, packed by the SDK from the build this test assembly was built with, in
/// <see cref="Directory"/>, the same with the fixture carrying its own copies of two shared assemblies, in
/// <see cref="CarryingDirectory"/>; its releases before and after, packed under the same package id by
/// <see cref="PackReleaseAsync"/>, the release before, which reads only version 1 of its family, in
/// <see cref="PreviousDirectory"/>, and the release after, which adds one migration, as <see cref="NextPackage"/>; and, in
/// <see cref="ClosureDirectory"/>, everything they and the EF provider engine depend on -
/// <c>Elsa.Persistence.EntityFramework</c>'s restore closure, EF Core, the Sqlite engine and what they need - copied from
/// the package cache this project's restore filled.
/// </summary>
/// <remarks>
/// The host carries EF Core, the engines and <c>Elsa.Persistence.EntityFramework</c> for its cluster membership (#2151), so
/// Nuplane must acquire none of what the closure feed offers but the engine package the <c>ef-provider</c> selection names:
/// the closure is there so that acquiring any of it is possible, and a test that finds one acquired has found a second copy.
/// No feed offers <c>Elsa.Persistence.EntityFramework</c> itself, or any other Elsa project: a host that did not provide one
/// would fail its reconciliation loudly rather than load a copy.
/// </remarks>
public sealed class FoundationHostFeed : IAsyncLifetime
{
    /// <summary>The fixture's package id, which all of its releases share.</summary>
    public const string FixturePackageId = "Elsa.Cluster.Fixtures.FeedModule";

    /// <summary>Packing a built project takes seconds; this is the ceiling for a <c>dotnet</c> that will never return.</summary>
    private static readonly TimeSpan DotnetTimeout = TimeSpan.FromMinutes(3);

    private static readonly string Fixture = Path.Join(FoundationHostProcess.RepoRoot, "tests", "essentials", "Cluster", "Fixtures", "FeedModule", $"{FixturePackageId}.csproj");

    /// <summary>The EF persistence project's restore: every package it needs, which the packed package declares.</summary>
    private static readonly string PersistenceAssets = Path.Join(FoundationHostProcess.RepoRoot, "src", "essentials", "Persistence", "EntityFramework", "obj", "project.assets.json");

    /// <summary>
    /// The restore of the test project this is compiled into (<c>Tests</c>, or <c>ProviderTests</c>, which links this file),
    /// which carries the engine it runs on and the rest of what it needs.
    /// </summary>
    private static readonly string TestAssets = Path.Join(
        FoundationHostProcess.RepoRoot, "tests", "essentials", "Cluster", "EntityFrameworkCore",
        typeof(FoundationHostFeed).Assembly.GetName().Name![(typeof(FoundationHostFeed).Assembly.GetName().Name!.LastIndexOf('.') + 1)..],
        "obj", "project.assets.json");

    /// <summary>The engine packages the tests run on; the restore of a project that does not use one does not name it.</summary>
    private static readonly string[] Engines = ["Microsoft.EntityFrameworkCore.Sqlite", "Npgsql.EntityFrameworkCore.PostgreSQL"];

    private readonly DirectoryInfo _root = System.IO.Directory.CreateTempSubdirectory("elsa-foundation-host-feeds-");

    /// <summary>The shares whose own copies the fixture's package in <see cref="CarryingDirectory"/> carries.</summary>
    public static readonly string[] CarriedShares = ["Elsa.Cluster.Core", "Elsa.Persistence.Schema"];

    /// <summary>The fixture's package, at the version that reads and writes version 2 of its family.</summary>
    public string Directory => Path.Join(_root.FullName, "packed");

    /// <summary>
    /// The same package, except that it carries its own copies of <see cref="CarriedShares"/> beside its own assembly, as a
    /// package that bundles what it was built against does (#2150). Nuplane loads every assembly of a host-integrated
    /// package graph unless the host's shared-assembly policy matches it.
    /// </summary>
    public string CarryingDirectory => Path.Join(_root.FullName, "carrying");

    /// <summary>The release of the fixture's package before it, which reads only version 1 of its family.</summary>
    public string PreviousDirectory => Path.Join(_root.FullName, "previous");

    /// <summary>The release before the fixture's, which a host started on <see cref="PreviousDirectory"/> loads.</summary>
    public string PreviousPackage => FoundationHostProcess.Releases(PreviousDirectory, FixturePackageId).Single();

    /// <summary>The fixture's own release, the package an in-place upgrade from <see cref="PreviousPackage"/> drops into a running host's feed.</summary>
    public string FixturePackage => FoundationHostProcess.Releases(Directory, FixturePackageId).Single();

    /// <summary>The release after the fixture's: the fixture plus a migration a host that validates has to have applied.</summary>
    public string NextPackage => FoundationHostProcess.Releases(Path.Join(_root.FullName, "next"), FixturePackageId).Single();

    /// <summary>The packages those depend on, which the host only resolves from.</summary>
    public string ClosureDirectory => Path.Join(_root.FullName, "closure");

    public async Task InitializeAsync()
    {
        await DotnetAsync("pack", Fixture, "--no-build", "-c", FoundationHostProcess.Configuration, "-p:IsPackable=true", "-o", Directory);
        foreach (var release in new[] { "previous", "next" })
            await PackReleaseAsync(Fixture, Path.Join(_root.FullName, "build", release), Path.Join(_root.FullName, release), $"FeedModuleRelease={release}");

        System.IO.Directory.CreateDirectory(CarryingDirectory);
        foreach (var package in System.IO.Directory.EnumerateFiles(Directory, "*.nupkg"))
            File.Copy(package, Path.Join(CarryingDirectory, Path.GetFileName(package)));
        AddCarriedShares(System.IO.Directory.EnumerateFiles(CarryingDirectory, $"{FixturePackageId}.*.nupkg").Single());

        System.IO.Directory.CreateDirectory(ClosureDirectory);
        // Both restores prune what ASP.NET's shared framework carries, which the host does not offer Nuplane, so the EF
        // persistence project's, a class library, is the one that names EF Core's own closure.
        foreach (var package in Packages(PersistenceAssets).Concat(Packages(TestAssets, Engines)).Distinct())
            File.Copy(package, Path.Join(ClosureDirectory, Path.GetFileName(package)), overwrite: true);
    }

    public Task DisposeAsync()
    {
        _root.Delete(recursive: true);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Adds the copies of <see cref="CarriedShares"/> this assembly was built with, the ones the fixture was compiled
    /// against, to the folder of <paramref name="package"/> that holds the fixture's own assembly.
    /// </summary>
    private static void AddCarriedShares(string package)
    {
        using var archive = ZipFile.Open(package, ZipArchiveMode.Update);
        var fixture = archive.Entries.Single(entry => entry.Name == $"{FixturePackageId}.dll");
        foreach (var share in CarriedShares)
            archive.CreateEntryFromFile(Path.Join(AppContext.BaseDirectory, $"{share}.dll"), $"{fixture.FullName[..^fixture.Name.Length]}{share}.dll");
    }

    /// <summary>
    /// The <c>.nupkg</c> of each of <paramref name="roots"/> and of everything they depend on, as the restore of
    /// <paramref name="assetsFile"/> resolved them; of every package it resolved when no root is named. Each is looked up in
    /// every package folder the restore names, as a restore reads from them all.
    /// </summary>
    private static IEnumerable<string> Packages(string assetsFile, params string[] roots)
    {
        var assets = JsonNode.Parse(File.ReadAllText(assetsFile))!;
        var folders = assets["packageFolders"]!.AsObject().Select(folder => folder.Key).ToArray();
        var libraries = assets["libraries"]!.AsObject();
        var target = assets["targets"]!.AsObject().Single().Value!.AsObject();
        var byId = target.ToDictionary(entry => entry.Key[..entry.Key.IndexOf('/')], entry => entry.Key, StringComparer.OrdinalIgnoreCase);

        var pending = new Stack<string>(roots.Length == 0 ? byId.Keys : roots);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (pending.TryPop(out var id))
        {
            if (!byId.TryGetValue(id, out var key) || !seen.Add(id) || libraries[key]!["type"]!.GetValue<string>() != "package")
                continue;

            var library = libraries[key]!;
            var file = library["files"]!.AsArray().Select(entry => entry!.GetValue<string>()).Single(name => name.EndsWith(".nupkg.sha512", StringComparison.Ordinal));
            var relative = Path.Join(library["path"]!.GetValue<string>(), file[..^".sha512".Length]);
            yield return folders.Select(folder => Path.Join(folder, relative)).FirstOrDefault(File.Exists)
                ?? throw new FileNotFoundException(
                    $"Package {id} {key[(key.IndexOf('/') + 1)..]} (from the restore in {assetsFile}) has no {Path.GetFileName(relative)} in the package folders searched: {string.Join(", ", folders)}. Restore the project again.");

            foreach (var dependency in target[key]!["dependencies"]?.AsObject().Select(entry => entry.Key) ?? [])
                pending.Push(dependency);
        }
    }

    /// <summary>
    /// Packs a release of the fixture <paramref name="project"/> other than the one its build left into
    /// <paramref name="output"/>, the one way every fixture with several releases is packed: <paramref name="properties"/>
    /// select the release's sources and version in the project, and <c>FixtureBuildRoot</c> builds it into
    /// <paramref name="buildRoot"/>, so the fixture's own build output, which the rest of the run shares, is left as it was.
    /// The project references are built already, by this project's build: building them again into these folders would
    /// pack their sources twice over and race the build everything else uses.
    /// </summary>
    internal static Task PackReleaseAsync(string project, string buildRoot, string output, params string[] properties) =>
        DotnetAsync(
        [
            "pack", project, "--no-restore", "-c", FoundationHostProcess.Configuration, "-p:IsPackable=true", "-p:BuildProjectReferences=false",
            $"-p:FixtureBuildRoot={buildRoot}{Path.DirectorySeparatorChar}", .. properties.Select(property => $"-p:{property}"), "-o", output
        ]);

    /// <summary>
    /// Runs <c>dotnet</c> to completion and fails with everything it wrote if it exits non-zero or outlasts
    /// <see cref="DotnetTimeout"/>, when its whole process tree is killed. MSBuild is kept from leaving nodes behind: they
    /// would inherit the redirected pipes, and reading them to the end would then never return.
    /// </summary>
    private static async Task DotnetAsync(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(FoundationHostProcess.DotnetPath, [.. arguments, "-nodeReuse:false"]);
        startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        startInfo.Environment["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0";

        var command = $"dotnet {string.Join(' ', startInfo.ArgumentList)}";
        var (exitCode, output) = await ChildProcess.RunAsync(startInfo, DotnetTimeout, command);
        if (exitCode != 0)
            throw new InvalidOperationException($"{command} exited {exitCode}. Its output:{Environment.NewLine}{output}");
    }
}
