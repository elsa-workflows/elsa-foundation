using System.Xml.Linq;
using Xunit;
using static Elsa.Architecture.Tests.RepoPaths;

namespace Elsa.Architecture.Tests;

/// <summary>
/// Holds public API tracking together (framework §2.23.3). A project tracks its public surface by setting
/// <c>&lt;ElsaTrackPublicApi&gt;true&lt;/ElsaTrackPublicApi&gt;</c> in its own project file; the root
/// <c>Directory.Build.props</c> then adds PublicApiAnalyzers and the project's <c>PublicAPI.Shipped.txt</c> and
/// <c>PublicAPI.Unshipped.txt</c>. The three must move together: an opt-in without its files, or files without the
/// opt-in, would leave the surface untracked while looking tracked.
/// </summary>
/// <remarks>
/// "Has the analyzer" is read from the project's committed <c>packages.lock.json</c>, which CI's locked restore holds to
/// what the project actually restores: if the wiring stopped adding the analyzer, the lock file would have to lose it
/// too, and this guard would see that.
/// </remarks>
public sealed class PublicApiFilesGuardTests
{
    private const string OptIn = "ElsaTrackPublicApi";
    private const string Shipped = "PublicAPI.Shipped.txt";
    private const string Unshipped = "PublicAPI.Unshipped.txt";
    private const string AnalyzerPackage = "Microsoft.CodeAnalysis.PublicApiAnalyzers";

    private static IReadOnlyList<ProjectApiState> Projects { get; } = Inspect(RepoRoot);

    [Fact]
    public void Every_project_that_tracks_its_public_api_has_both_files_and_the_analyzer()
    {
        var violations = IncompleteTracking(Projects);

        Assert.True(violations.Count == 0,
            $"A project that sets {OptIn} must carry {Shipped}, {Unshipped} and the analyzer, and set the opt-in " +
            $"unconditionally:{Environment.NewLine}{string.Join(Environment.NewLine, violations)}");
    }

    [Fact]
    public void Every_project_with_a_publicapi_file_tracks_its_public_api()
    {
        var violations = UntrackedFiles(Projects);

        Assert.True(violations.Count == 0,
            $"A project carrying {Shipped} or {Unshipped} must set {OptIn}, or the files track nothing: " +
            string.Join(", ", violations));
    }

    [Fact]
    public void Only_packable_production_projects_track_their_public_api()
    {
        var violations = TrackingOutsidePackableProduction(Projects);

        Assert.True(violations.Count == 0,
            $"Only a packable project under Essentials/src or Extensions/*/src has a public surface to track: {string.Join(", ", violations)}");
    }

    /// <summary>
    /// Pins that the sweep is not vacuous: it reads test and production projects alike, and it sees the projects that
    /// track today.
    /// </summary>
    [Fact]
    public void The_sweep_covers_every_module_root_and_sees_the_tracking_projects()
    {
        Assert.Contains(Projects, project => project is { Name: "Elsa.Primitives", Tracks: true });
        Assert.Contains(Projects, project => project is { Name: "Elsa.Events.Core", Tracks: true });
        Assert.Contains(Projects, project => project.RelativeDirectory.StartsWith("Essentials/tests/", StringComparison.Ordinal));
        Assert.Contains(Projects, project => project.RelativeDirectory.StartsWith("Essentials/apps/", StringComparison.Ordinal));
        Assert.Contains(Projects, project => project.RelativeDirectory.StartsWith("Extensions/", StringComparison.Ordinal));
        Assert.True(Projects.Count(project => !project.Tracks) > Projects.Count(project => project.Tracks),
            "Expected untracked projects (tests at least) to outnumber tracked ones; the sweep may have stopped finding them.");
    }

    /// <summary>The three detectors, against a sandbox of projects in every state they must tell apart.</summary>
    [Fact]
    public void Each_inconsistent_state_is_flagged_and_the_consistent_one_is_not()
    {
        using var sandbox = new Sandbox();
        sandbox.Project("Essentials/src/Good/Core", tracks: true, shipped: true, unshipped: true, lockedAnalyzer: true);
        sandbox.Project("Essentials/src/MissingUnshipped/Core", tracks: true, shipped: true, unshipped: false, lockedAnalyzer: true);
        sandbox.Project("Essentials/src/NoAnalyzer/Core", tracks: true, shipped: true, unshipped: true, lockedAnalyzer: false);
        sandbox.Project("Essentials/src/Conditional/Core", tracks: true, shipped: true, unshipped: true, lockedAnalyzer: true, conditional: true);
        sandbox.Project("Essentials/src/FilesOnly/Core", tracks: false, shipped: true, unshipped: false, lockedAnalyzer: false);
        sandbox.Project("Essentials/src/Untracked/Core", tracks: false, shipped: false, unshipped: false, lockedAnalyzer: false);
        sandbox.Project("Essentials/tests/Good/Tests", tracks: true, shipped: true, unshipped: true, lockedAnalyzer: true);
        sandbox.Project("Essentials/src/Hidden/Core", tracks: true, shipped: true, unshipped: true, lockedAnalyzer: true, notPackable: true);

        var projects = Inspect(sandbox.Root);

        Assert.Equal(
        [
            "Elsa.Conditional.Core: sets ElsaTrackPublicApi under a Condition",
            "Elsa.MissingUnshipped.Core: missing PublicAPI.Unshipped.txt",
            "Elsa.NoAnalyzer.Core: its packages.lock.json does not list Microsoft.CodeAnalysis.PublicApiAnalyzers",
        ],
            IncompleteTracking(projects));
        Assert.Equal(["Elsa.FilesOnly.Core"], UntrackedFiles(projects));
        Assert.Equal(["Elsa.Good.Tests", "Elsa.Hidden.Core"], TrackingOutsidePackableProduction(projects));
    }

    private static IReadOnlyList<string> IncompleteTracking(IEnumerable<ProjectApiState> projects) =>
    [
        .. projects.Where(project => project.Tracks).SelectMany(project => new[]
            {
                project.OptInIsConditional ? $"{project.Name}: sets {OptIn} under a Condition" : null,
                project.HasShipped ? null : $"{project.Name}: missing {Shipped}",
                project.HasUnshipped ? null : $"{project.Name}: missing {Unshipped}",
                project.LockListsAnalyzer ? null : $"{project.Name}: its packages.lock.json does not list {AnalyzerPackage}",
            })
            .OfType<string>()
            .Order(StringComparer.Ordinal)
    ];

    private static IReadOnlyList<string> UntrackedFiles(IEnumerable<ProjectApiState> projects) =>
    [
        .. projects.Where(project => !project.Tracks && (project.HasShipped || project.HasUnshipped))
            .Select(project => project.Name)
            .Order(StringComparer.Ordinal)
    ];

    private static IReadOnlyList<string> TrackingOutsidePackableProduction(IEnumerable<ProjectApiState> projects) =>
    [
        .. projects.Where(project => project.Tracks && !project.IsPackableProduction)
            .Select(project => project.Name)
            .Order(StringComparer.Ordinal)
    ];

    /// <summary>Every project under <see cref="ModuleRoots.All"/> of <paramref name="repoRoot"/>, with its tracking state.</summary>
    private static IReadOnlyList<ProjectApiState> Inspect(string repoRoot) =>
    [
        .. ModuleRoots.Resolve(repoRoot, ModuleRoots.All)
            .SelectMany(root => Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories))
            .Where(path => !IsBuildOutput(repoRoot, path))
            .Select(path => ProjectApiState.Read(repoRoot, path))
            .OrderBy(project => project.Name, StringComparer.Ordinal)
    ];

    private sealed record ProjectApiState(
        string Name,
        string RelativeDirectory,
        bool Tracks,
        bool OptInIsConditional,
        bool HasShipped,
        bool HasUnshipped,
        bool LockListsAnalyzer,
        bool IsPackableProduction)
    {
        public static ProjectApiState Read(string repoRoot, string projectPath)
        {
            var directory = Path.GetDirectoryName(projectPath)!;
            var relativeDirectory = Path.GetRelativePath(repoRoot, directory).Replace('\\', '/') + "/";
            var project = XDocument.Load(projectPath);
            var optIn = project.Descendants().Where(element => element.Name.LocalName == OptIn).ToArray();
            var lockFile = Path.Join(directory, "packages.lock.json");
            var isProduction = relativeDirectory.StartsWith("Essentials/src/", StringComparison.Ordinal) ||
                               (relativeDirectory.StartsWith("Extensions/", StringComparison.Ordinal) && !ElsaDomains.IsTestPath(relativeDirectory) &&
                                relativeDirectory.Split('/') is [_, _, "src", ..]);

            return new ProjectApiState(
                Path.GetFileNameWithoutExtension(projectPath),
                relativeDirectory,
                Tracks: optIn.Any(element => string.Equals(element.Value.Trim(), "true", StringComparison.OrdinalIgnoreCase)),
                OptInIsConditional: optIn.Any(element => element.AncestorsAndSelf().Any(ancestor => ancestor.Attribute("Condition") is not null)),
                HasShipped: File.Exists(Path.Join(directory, Shipped)),
                HasUnshipped: File.Exists(Path.Join(directory, Unshipped)),
                LockListsAnalyzer: File.Exists(lockFile) && File.ReadAllText(lockFile).Contains($"\"{AnalyzerPackage}\"", StringComparison.Ordinal),
                IsPackableProduction: isProduction && !string.Equals(
                    project.Descendants().LastOrDefault(element => element.Name.LocalName == "IsPackable")?.Value.Trim(),
                    "false",
                    StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>A throwaway module tree for the detector proof, cleaned up on dispose.</summary>
    private sealed class Sandbox : IDisposable
    {
        public string Root { get; } = Directory.CreateTempSubdirectory("elsa-publicapi-guard-").FullName;

        public void Project(
            string relativeDirectory,
            bool tracks,
            bool shipped,
            bool unshipped,
            bool lockedAnalyzer,
            bool conditional = false,
            bool notPackable = false)
        {
            var directory = Path.Join(Root, relativeDirectory);
            Directory.CreateDirectory(directory);
            var segments = relativeDirectory.Split('/');
            var name = $"Elsa.{segments[2]}.{segments[^1]}";
            var condition = conditional ? " Condition=\"'$(Configuration)' == 'Release'\"" : string.Empty;
            var properties = (tracks ? $"<{OptIn}>true</{OptIn}>" : string.Empty) + (notPackable ? "<IsPackable>false</IsPackable>" : string.Empty);

            File.WriteAllText(Path.Join(directory, $"{name}.csproj"),
                $"""<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup{condition}>{properties}</PropertyGroup></Project>""");
            if (shipped)
                File.WriteAllText(Path.Join(directory, Shipped), "#nullable enable\n");
            if (unshipped)
                File.WriteAllText(Path.Join(directory, Unshipped), "#nullable enable\n");
            File.WriteAllText(Path.Join(directory, "packages.lock.json"),
                lockedAnalyzer ? $$"""{ "dependencies": { "net10.0": { "{{AnalyzerPackage}}": { "type": "Direct" } } } }""" : "{}");
        }

        public void Dispose() => Directory.Delete(Root, recursive: true);
    }
}
