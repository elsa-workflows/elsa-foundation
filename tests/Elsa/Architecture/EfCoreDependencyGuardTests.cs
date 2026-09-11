using System.Xml.Linq;
using Xunit;

namespace Elsa.Architecture.Tests;

/// <summary>
/// First-party EF Core persistence was removed with issue #1482: Elsa ships Groundwork-only stores, so
/// EF Core may only survive where the <c>Elsa.Workbench</c> host wires the vendor OpenIddict store it
/// chose for itself. The guard walks the declared csproj graph (no restore, no baseline) and scans
/// sources, so a new EF edge anywhere else under <c>src/</c> fails and names the offender. To let another
/// host own an EF vendor store, add it to <see cref="AllowedEfConsumers"/> in the same change. The proposed
/// ADR 0072 Secrets EF pilot trees are exempt themselves (see <see cref="Adr0072SecretsEfPilot"/>), but a
/// project outside them that reaches EF through a pilot project still fails.
/// </summary>
public sealed class EfCoreDependencyGuardTests
{
    private static readonly string[] AllowedEfConsumers = ["Elsa.Workbench"];

    private static readonly string[] EfPackagePrefixes =
        ["Microsoft.EntityFrameworkCore", "OpenIddict.EntityFrameworkCore", "Microsoft.AspNetCore.Identity.EntityFrameworkCore"];

    private static readonly string RepoRoot = FindRepoRoot();
    private static readonly Dictionary<string, Project> Projects = LoadSrcProjects();

    [Fact]
    public void Only_the_allowed_consumers_and_their_dependents_reach_ef_core_packages()
    {
        Assert.All(AllowedEfConsumers, name => Assert.Contains(name, Projects.Keys));

        var reachingEf = Projects.Values
            .Where(project => Reachable([project.Name], name => Projects[name].References).Any(name => Projects[name].DeclaresEf))
            .Select(project => project.Name);

        var offenders = reachingEf.Except(AllowedClosure()).Where(name => !Projects[name].IsPilot).Order().ToArray();

        Assert.True(offenders.Length == 0, Report("reach an EF Core package outside the allowed consumers and their dependents", offenders));
    }

    [Fact]
    public void No_project_outside_the_allowed_closure_declares_an_ef_core_package()
    {
        var offenders = Projects.Values
            .Where(project => project.DeclaresEf && !project.IsPilot)
            .Select(project => project.Name)
            .Except(AllowedClosure())
            .Order()
            .ToArray();

        Assert.True(offenders.Length == 0, Report("declare an EF Core PackageReference directly", offenders));
    }

    [Fact]
    public void No_source_file_outside_the_allowed_consumers_mentions_ef_core()
    {
        var offenders = Directory.EnumerateFiles(Path.Combine(RepoRoot, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(file => !IsBuildOutput(file) && !AllowedEfConsumers.Contains(OwningProject(file)))
            .Where(file => !Adr0072SecretsEfPilot.IsSurfacePath(Path.GetRelativePath(RepoRoot, file).Replace('\\', '/')))
            .Where(file => File.ReadAllText(file).Contains("Microsoft.EntityFrameworkCore", StringComparison.Ordinal))
            .Select(file => Path.GetRelativePath(RepoRoot, file))
            .Order()
            .ToArray();

        Assert.True(offenders.Length == 0, Report("mention Microsoft.EntityFrameworkCore in source", offenders));
    }

    // The allowed consumers plus every src project that (transitively) references one of them.
    private static HashSet<string> AllowedClosure() =>
        Reachable(AllowedEfConsumers, name => Projects.Values.Where(p => p.References.Contains(name)).Select(p => p.Name));

    private static HashSet<string> Reachable(IEnumerable<string> roots, Func<string, IEnumerable<string>> next)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>(roots);
        while (pending.TryPop(out var current))
            if (visited.Add(current))
                foreach (var name in next(current).Where(Projects.ContainsKey))
                    pending.Push(name);
        return visited;
    }

    private static Dictionary<string, Project> LoadSrcProjects()
    {
        var projects = new Dictionary<string, Project>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(Path.Combine(RepoRoot, "src"), "*.csproj", SearchOption.AllDirectories).Where(f => !IsBuildOutput(f)))
        {
            var document = XDocument.Load(file);
            var references = document.Descendants("ProjectReference")
                .Select(x => Path.GetFileNameWithoutExtension((x.Attribute("Include")?.Value ?? "").Replace('\\', '/')))
                .ToHashSet(StringComparer.Ordinal);
            var declaresEf = document.Descendants("PackageReference")
                .Any(x => EfPackagePrefixes.Any(prefix => (x.Attribute("Include")?.Value ?? "").StartsWith(prefix, StringComparison.OrdinalIgnoreCase)));
            var isPilot = Adr0072SecretsEfPilot.IsSurfacePath(Path.GetRelativePath(RepoRoot, file).Replace('\\', '/'));
            projects.Add(Path.GetFileNameWithoutExtension(file), new Project(Path.GetFileNameWithoutExtension(file), references, declaresEf, isPilot));
        }
        return projects;
    }

    private static string OwningProject(string file)
    {
        for (var directory = Path.GetDirectoryName(file); directory is not null && directory.StartsWith(RepoRoot, StringComparison.Ordinal); directory = Path.GetDirectoryName(directory))
        {
            var project = Directory.EnumerateFiles(directory, "*.csproj").FirstOrDefault();
            if (project is not null)
                return Path.GetFileNameWithoutExtension(project);
        }
        return "";
    }

    private static bool IsBuildOutput(string path) => path.Replace('\\', '/') is var p && (p.Contains("/bin/") || p.Contains("/obj/"));

    private static string Report(string what, IEnumerable<string> offenders) =>
        $"These src entries {what}:{Environment.NewLine}{string.Join(Environment.NewLine, offenders)}";

    private static string FindRepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "Elsa.Server.slnx")))
                return directory.FullName;
        throw new DirectoryNotFoundException("Could not find repository root.");
    }

    private sealed record Project(string Name, HashSet<string> References, bool DeclaresEf, bool IsPilot);

    /// <summary>
    /// ADR 0072 (proposed) Secrets EF pilot. ADR 0042 still permits only the OpenIddict vendor exception; this
    /// prefix exemption is a review-time carve-out, not an accepted amendment.
    /// <see cref="SecretsEfPersistencePilotArchitectureTests"/> owns the exact package and source inventory.
    /// </summary>
    internal static class Adr0072SecretsEfPilot
    {
        public static readonly string[] SurfacePathPrefixes =
        [
            "src/Elsa/Persistence/EntityFramework/",
            "src/Elsa/Secrets/Persistence/EntityFrameworkCore/",
            "tests/Elsa/Persistence/EntityFramework/",
            "tests/Elsa/Secrets/Persistence/EntityFrameworkCore/"
        ];

        public static bool IsSurfacePath(string relativePath) =>
            SurfacePathPrefixes.Any(prefix => relativePath.StartsWith(prefix, StringComparison.Ordinal));
    }
}
