using System.Diagnostics;
using System.Text.Json.Nodes;

namespace Elsa.Cluster.EntityFrameworkCore.Tests;

/// <summary>
/// The feeds the host loads from: the fixture and <c>Elsa.Persistence.EntityFramework</c>, packed by the SDK from the build
/// this test assembly was built with, in <see cref="Directory"/>; and the packages they and the EF provider engine depend
/// on that the host does not carry (EF Core, the Sqlite engine and what they need), copied from the package cache this
/// project's restore filled, in <see cref="ClosureDirectory"/>. Two other releases of the fixture are built and packed
/// here, so a host can be upgraded in place: its previous release, a lower version whose family reads version 1 alone,
/// beside the persistence package in <see cref="PreviousDirectory"/>, which a host starts on; and its next release, a
/// higher version that adds one migration, as <see cref="NextPackage"/>.
/// </summary>
public sealed class FoundationHostFeed : IAsyncLifetime
{
    /// <summary>The fixture's package id, which all of its releases share.</summary>
    public const string FixturePackageId = "Elsa.Cluster.Fixtures.FeedModule";

    /// <summary>Packing a built project takes seconds; this is the ceiling for a <c>dotnet</c> that will never return.</summary>
    private static readonly TimeSpan DotnetTimeout = TimeSpan.FromMinutes(3);

    private static readonly string FixtureProject = Path.Join(FoundationHostProcess.RepoRoot, "tests", "essentials", "Cluster", "Fixtures", "FeedModule", $"{FixturePackageId}.csproj");

    private static readonly string[] Projects =
    [
        FixtureProject,
        Path.Join(FoundationHostProcess.RepoRoot, "src", "essentials", "Persistence", "EntityFramework", "Elsa.Persistence.EntityFramework.csproj")
    ];

    /// <summary>The EF persistence project's restore: every package it needs, which the packed package declares.</summary>
    private static readonly string PersistenceAssets = Path.Join(FoundationHostProcess.RepoRoot, "src", "essentials", "Persistence", "EntityFramework", "obj", "project.assets.json");

    /// <summary>This project's restore, which carries the Sqlite engine and the rest of what it needs.</summary>
    private static readonly string TestAssets = Path.Join(FoundationHostProcess.RepoRoot, "tests", "essentials", "Cluster", "EntityFrameworkCore", "Tests", "obj", "project.assets.json");

    private readonly DirectoryInfo _root = System.IO.Directory.CreateTempSubdirectory("elsa-foundation-host-feeds-");

    /// <summary>The packed packages, which the host takes as its own feed.</summary>
    public string Directory => Path.Join(_root.FullName, "packed");

    /// <summary>The fixture's previous release and the persistence package, which a host starts on before it is upgraded.</summary>
    public string PreviousDirectory => Path.Join(_root.FullName, "previous");

    /// <summary>The fixture's previous release, which a host started on <see cref="PreviousDirectory"/> loads first.</summary>
    public string PreviousPackage => FoundationHostProcess.Releases(PreviousDirectory, FixturePackageId).Single();

    /// <summary>The fixture's current release, the package an in-place upgrade drops into a running host's feed.</summary>
    public string FixturePackage => FoundationHostProcess.Releases(Directory, FixturePackageId).Single();

    /// <summary>The fixture's next release: the current one plus a migration a host that validates has to have applied.</summary>
    public string NextPackage => FoundationHostProcess.Releases(Path.Join(_root.FullName, "next"), FixturePackageId).Single();

    /// <summary>The packages those depend on, which the host only resolves from.</summary>
    public string ClosureDirectory => Path.Join(_root.FullName, "closure");

    public async Task InitializeAsync()
    {
        foreach (var project in Projects)
            await DotnetAsync("pack", project, "--no-build", "-c", FoundationHostProcess.Configuration, "-p:IsPackable=true", "-o", Directory);

        // The other releases are compiled here, each into outputs of its own: only the fixture, against the persistence
        // build the current release was built against. The previous one is packed with the persistence package it needs.
        foreach (var release in new[] { "previous", "next" })
            await DotnetAsync("pack", FixtureProject, "--no-restore", "-c", FoundationHostProcess.Configuration, $"-p:FeedModuleRelease={release}",
                "-p:BuildProjectReferences=false", "-p:IsPackable=true", "-o", Path.Join(_root.FullName, release));
        foreach (var package in System.IO.Directory.EnumerateFiles(Directory, "*.nupkg").Except(FoundationHostProcess.Releases(Directory, FixturePackageId)))
            File.Copy(package, Path.Join(PreviousDirectory, Path.GetFileName(package)));

        System.IO.Directory.CreateDirectory(ClosureDirectory);
        // Both restores prune what ASP.NET's shared framework carries, which the host does not offer Nuplane, so the EF
        // persistence project's, a class library, is the one that names EF Core's own closure.
        foreach (var package in Packages(PersistenceAssets).Concat(Packages(TestAssets, "Microsoft.EntityFrameworkCore.Sqlite")).Distinct())
            File.Copy(package, Path.Join(ClosureDirectory, Path.GetFileName(package)), overwrite: true);
    }

    public Task DisposeAsync()
    {
        _root.Delete(recursive: true);
        return Task.CompletedTask;
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
    /// Runs <c>dotnet</c> to completion and fails with everything it wrote if it exits non-zero or outlasts
    /// <see cref="DotnetTimeout"/>, when its whole process tree is killed. MSBuild is kept from leaving nodes behind: they
    /// would inherit the redirected pipes, and reading them to the end would then never return.
    /// </summary>
    private static async Task DotnetAsync(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo(FoundationHostProcess.DotnetPath, [.. arguments, "-nodeReuse:false"])
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        startInfo.Environment["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0";

        var output = new CapturedOutput();
        using var process = new Process { StartInfo = startInfo };
        process.OutputDataReceived += (_, line) => output.Append(line.Data);
        process.ErrorDataReceived += (_, line) => output.Append(line.Data);
        var command = $"dotnet {string.Join(' ', startInfo.ArgumentList)}";

        using var timeout = new CancellationTokenSource(DotnetTimeout);
        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"{command} did not finish within {DotnetTimeout} and was killed. Its output:{Environment.NewLine}{output}");
        }

        if (process.ExitCode != 0)
            throw new InvalidOperationException($"{command} exited {process.ExitCode}. Its output:{Environment.NewLine}{output}");
    }
}
