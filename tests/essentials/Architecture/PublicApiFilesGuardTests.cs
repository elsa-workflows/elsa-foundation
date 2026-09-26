using Xunit;

namespace Elsa.Architecture.Tests;

/// <summary>
/// Keeps PublicApiAnalyzers' declared-API files where issue #2078 put them: every Line A project carries
/// both <c>PublicAPI.Shipped.txt</c> and <c>PublicAPI.Unshipped.txt</c>, and no Line B project carries
/// either, because <c>Directory.Build.props</c> wires the analyzer and its <c>AdditionalFiles</c> off
/// <c>$(ElsaVersionLine) == 'A'</c> alone.
/// </summary>
/// <remarks>
/// Line A membership is read from <c>VersionLines.props</c> via <see cref="VersionLines.LineAMembers"/>, the
/// same source <see cref="LineAClosureGuardTests"/> uses, rather than restated here. The sweep covers every
/// <c>src/</c> root <see cref="ModuleRoots.Production"/> resolves — <c>src/essentials</c>, <c>src/extensions</c>
/// and <c>src/apps</c> — so a Line B project anywhere in the tree is caught, not just the ones under
/// <c>src/essentials</c> where all ten Line A projects happen to live.
/// </remarks>
public sealed class PublicApiFilesGuardTests
{
    private static string RepoRoot { get; } = FindRepoRoot();

    private static IReadOnlyList<string> LineAMembers { get; } = VersionLines.LineAMembers(RepoRoot);

    /// <summary>Every Elsa project under <c>src/</c>, by name, with the directory its <c>.csproj</c> lives in.</summary>
    private static IReadOnlyDictionary<string, string> ElsaProjectDirectories { get; } = LoadElsaProjectDirectories(RepoRoot);

    private static IReadOnlyDictionary<string, string> LineADirectories { get; } =
        ElsaProjectDirectories.Where(project => LineAMembers.Contains(project.Key, StringComparer.OrdinalIgnoreCase))
            .ToDictionary(project => project.Key, project => project.Value, StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyDictionary<string, string> LineBDirectories { get; } =
        ElsaProjectDirectories.Where(project => !LineAMembers.Contains(project.Key, StringComparer.OrdinalIgnoreCase))
            .ToDictionary(project => project.Key, project => project.Value, StringComparer.OrdinalIgnoreCase);

    [Fact]
    public void Every_line_a_project_has_both_publicapi_files()
    {
        var missing = MissingPublicApiFiles(LineADirectories);

        Assert.True(missing.Count == 0,
            "Every Line A project must carry PublicAPI.Shipped.txt and PublicAPI.Unshipped.txt (#2078). " +
            $"Missing from: {string.Join(", ", missing)}.");
    }

    [Fact]
    public void No_line_b_project_has_publicapi_files()
    {
        var unexpected = ProjectsWithPublicApiFiles(LineBDirectories);

        Assert.True(unexpected.Count == 0,
            "Only Line A projects carry PublicAPI.Shipped.txt/PublicAPI.Unshipped.txt (#2078); a Line B " +
            $"project should never gain them. Found on: {string.Join(", ", unexpected)}.");
    }

    /// <summary>
    /// Pins that the sweep is not vacuous: it names more than ten Elsa projects outside Line A, and it found
    /// at least one under each of the three roots <see cref="ModuleRoots.Production"/> resolves. A guard
    /// scoped only to <c>src/essentials</c> — where every Line A project happens to live — would still read
    /// green if <c>src/extensions</c> or <c>src/apps</c> silently grew a Line B project carrying these files.
    /// </summary>
    [Fact]
    public void The_scan_covers_a_nontrivial_line_b_universe_across_every_src_root()
    {
        Assert.NotEmpty(LineAMembers);
        Assert.True(LineBDirectories.Count > LineAMembers.Count,
            $"Expected more Line B Elsa projects than the {LineAMembers.Count} Line A members, to prove this " +
            $"guard checks a real, non-trivial set; found {LineBDirectories.Count}.");

        var essentialsRoot = Path.Join(RepoRoot, "src", "essentials");
        var extensionsRoot = Path.Join(RepoRoot, "src", "extensions");
        var appsRoot = Path.Join(RepoRoot, "src", "apps");

        Assert.Contains(LineBDirectories.Values, dir => dir.StartsWith(essentialsRoot, StringComparison.Ordinal));
        Assert.Contains(LineBDirectories.Values, dir => dir.StartsWith(extensionsRoot, StringComparison.Ordinal));
        Assert.Contains(LineBDirectories.Values, dir => dir.StartsWith(appsRoot, StringComparison.Ordinal));
    }

    /// <summary>The detectors themselves, so the two guards above going green means "checked" rather than "skipped".</summary>
    [Fact]
    public void A_line_a_project_missing_either_file_is_flagged()
    {
        using var sandbox = new TempSandbox();

        var complete = sandbox.CreateProjectDirectory("Complete");
        File.WriteAllText(Path.Join(complete, "PublicAPI.Shipped.txt"), "#nullable enable\n");
        File.WriteAllText(Path.Join(complete, "PublicAPI.Unshipped.txt"), "#nullable enable\n");

        var missingUnshipped = sandbox.CreateProjectDirectory("MissingUnshipped");
        File.WriteAllText(Path.Join(missingUnshipped, "PublicAPI.Shipped.txt"), "#nullable enable\n");

        var missingBoth = sandbox.CreateProjectDirectory("MissingBoth");

        var directories = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Elsa.Complete.Core"] = complete,
            ["Elsa.MissingUnshipped.Core"] = missingUnshipped,
            ["Elsa.MissingBoth.Core"] = missingBoth,
        };

        Assert.Equal(
            ["Elsa.MissingBoth.Core", "Elsa.MissingUnshipped.Core"],
            MissingPublicApiFiles(directories));

        Assert.Empty(MissingPublicApiFiles(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Elsa.Complete.Core"] = complete
        }));
    }

    [Fact]
    public void A_line_b_project_carrying_either_file_is_flagged()
    {
        using var sandbox = new TempSandbox();

        var clean = sandbox.CreateProjectDirectory("Clean");

        var carriesShipped = sandbox.CreateProjectDirectory("CarriesShipped");
        File.WriteAllText(Path.Join(carriesShipped, "PublicAPI.Shipped.txt"), "#nullable enable\n");

        var carriesUnshipped = sandbox.CreateProjectDirectory("CarriesUnshipped");
        File.WriteAllText(Path.Join(carriesUnshipped, "PublicAPI.Unshipped.txt"), "#nullable enable\n");

        var directories = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Elsa.Clean"] = clean,
            ["Elsa.CarriesShipped"] = carriesShipped,
            ["Elsa.CarriesUnshipped"] = carriesUnshipped,
        };

        Assert.Equal(
            ["Elsa.CarriesShipped", "Elsa.CarriesUnshipped"],
            ProjectsWithPublicApiFiles(directories));

        Assert.Empty(ProjectsWithPublicApiFiles(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Elsa.Clean"] = clean
        }));
    }

    /// <summary>Every named project directory missing PublicAPI.Shipped.txt or PublicAPI.Unshipped.txt, by name.</summary>
    private static IReadOnlyList<string> MissingPublicApiFiles(IReadOnlyDictionary<string, string> directories) =>
        [
            .. directories
                .Where(project => !HasBothPublicApiFiles(project.Value))
                .Select(project => project.Key)
                .Order(StringComparer.Ordinal)
        ];

    /// <summary>Every named project directory carrying either PublicAPI file, by name.</summary>
    private static IReadOnlyList<string> ProjectsWithPublicApiFiles(IReadOnlyDictionary<string, string> directories) =>
        [
            .. directories
                .Where(project => HasEitherPublicApiFile(project.Value))
                .Select(project => project.Key)
                .Order(StringComparer.Ordinal)
        ];

    private static bool HasBothPublicApiFiles(string projectDirectory) =>
        File.Exists(Path.Join(projectDirectory, "PublicAPI.Shipped.txt")) &&
        File.Exists(Path.Join(projectDirectory, "PublicAPI.Unshipped.txt"));

    private static bool HasEitherPublicApiFile(string projectDirectory) =>
        File.Exists(Path.Join(projectDirectory, "PublicAPI.Shipped.txt")) ||
        File.Exists(Path.Join(projectDirectory, "PublicAPI.Unshipped.txt"));

    /// <summary>Every Elsa project under <see cref="ModuleRoots.Production"/>, by name, mapped to its directory.</summary>
    private static IReadOnlyDictionary<string, string> LoadElsaProjectDirectories(string repoRoot) =>
        ModuleRoots.Resolve(repoRoot, ModuleRoots.Production)
            .SelectMany(root => Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories))
            .Where(ModuleRoots.IsNotTestFile)
            .Where(path => ProjectGraph.IsElsa(Path.GetFileNameWithoutExtension(path)))
            .ToDictionary(
                Path.GetFileNameWithoutExtension,
                path => Path.GetDirectoryName(Path.GetFullPath(path))!,
                StringComparer.OrdinalIgnoreCase)!;

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Join(directory.FullName, "Elsa.Server.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate the repository root.");
    }

    /// <summary>A throwaway directory tree for the detector-proof tests, cleaned up on dispose.</summary>
    private sealed class TempSandbox : IDisposable
    {
        private readonly string _root = Directory.CreateTempSubdirectory("elsa-publicapi-guard-").FullName;

        public string CreateProjectDirectory(string name)
        {
            var path = Path.Join(_root, name);
            Directory.CreateDirectory(path);
            return path;
        }

        public void Dispose() => Directory.Delete(_root, recursive: true);
    }
}
