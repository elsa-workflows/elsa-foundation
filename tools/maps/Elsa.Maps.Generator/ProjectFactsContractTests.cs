using static Elsa.Maps.Generator.ContractTestRepository;

namespace Elsa.Maps.Generator;

/// <summary>
/// Dependency-free contract tests for the project graph's packable precedence, package-id resolution,
/// Line A/B whole-name matching, and the reading of pinned-transitive edges from NuGet lock files. Keeps
/// them pinned against fixtures rather than only against whatever the live tree happens to contain.
/// </summary>
public static class ProjectFactsContractTests
{
    public static void Run()
    {
        var root = Path.Join(Path.GetTempPath(), $"elsa-project-facts-tests-{Environment.ProcessId}-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(root);
            WriteFixture(root);
            GitInit(root);

            var repo = RepoContext.Discover(root);
            var facts = ProjectGraph.Read(repo).ToDictionary(fact => fact.Name, StringComparer.Ordinal);

            // Precedence level 1: the project file itself, overriding a nearer Directory.Build.props that
            // declares the opposite. No live project exercises this direction, so a fixture is the only
            // coverage.
            AssertPackable(facts, "ProjectFileOverridesInheritedFalse", true,
                "A project-file IsPackable=true must win over an inherited Directory.Build.props IsPackable=false.");

            // Precedence level 2: the nearest Directory.Build.props, overriding the SDK/test defaults that
            // would otherwise make the project packable.
            AssertPackable(facts, "DirectoryBuildPropsOverridesDefault", false,
                "A Directory.Build.props IsPackable=false must win when the project file declares nothing.");

            // Precedence level 3: the Web/Worker SDK default.
            AssertPackable(facts, "WebSdkDefaultsToFalse", false,
                "The Microsoft.NET.Sdk.Web default must be false when nothing overrides it.");

            // Precedence level 4a: IsTestProject.
            AssertPackable(facts, "TestProjectFlagIsFalse", false,
                "IsTestProject=true must make an otherwise-packable project not packable.");

            // Precedence level 4b: a Microsoft.NET.Test.Sdk package reference.
            AssertPackable(facts, "TestSdkReferenceIsFalse", false,
                "A Microsoft.NET.Test.Sdk PackageReference must make an otherwise-packable project not packable.");

            // Precedence level 5: the true default when nothing above applies.
            AssertPackable(facts, "DefaultIsPackable", true,
                "A project with no IsPackable anywhere and no SDK/test signal must default to packable.");

            // Package-id resolution: PackageId, then AssemblyName, then the project name.
            AssertPackageId(facts, "PackageIdWins", "Custom.Package.Id");
            AssertPackageId(facts, "AssemblyNameWins", "Custom.Assembly.Name");
            AssertPackageId(facts, "ProjectNameWins", "ProjectNameWins");

            // Whole-name Line A matching: "Elsa.Tasks" must not match "Elsa.Tasks.Core".
            AssertLine(facts, "Fixture.LineA.Core", "A");
            AssertLine(facts, "Fixture.LineA", "B");

            // Pinned-transitive edges (spec 149 FR-012) come from the committed lock file: its CentralTransitive entries that
            // a reference which is not private reaches, through a package or a project, and nothing a runtime-specific
            // target adds. Private.Pin is reached only through a PrivateAssets="all" reference, which keeps it out of the
            // nuspec, so it is left out; made public, the same reference brings it in.
            AssertPinnedTransitive(repo, "PinnedTransitive", "Transitive.A 1.0.0, Transitive.B 2.0.0");
            WriteProject(root, "src/PinnedTransitive", PinnedTransitiveProject.Replace(" PrivateAssets=\"all\"", string.Empty, StringComparison.Ordinal));
            AssertPinnedTransitive(repo, "PinnedTransitive", "Private.Pin 6.0.0, Transitive.A 1.0.0, Transitive.B 2.0.0");
            WriteProject(root, "src/PinnedTransitive", PinnedTransitiveProject.Replace("DefaultIsPackable.csproj\" />", "DefaultIsPackable.csproj\"><PrivateAssets>All</PrivateAssets></ProjectReference>", StringComparison.Ordinal));
            AssertPinnedTransitive(repo, "PinnedTransitive", "Transitive.A 1.0.0");
            WriteProject(root, "src/PinnedTransitive", PinnedTransitiveProject);

            // A lock file the tree has moved past is refused rather than read, since a stale list would leave a package's
            // nuspec change undetected: a pin, a reference or the project's references changed without it.
            AssertRefused(repo, "DefaultIsPackable", "has no NuGet lock file");
            WriteCentralPackages(root, ("Transitive.A", "1.0.1"));
            AssertRefused(repo, "PinnedTransitive", "not the version Directory.Packages.props pins");
            WriteCentralPackages(root, ("Direct.Package", "3.0.1"));
            AssertRefused(repo, "PinnedTransitive", "locks Direct.Package at [3.0.0, )");
            WriteCentralPackages(root, ("Unpinned.Dependency", "1.0.0"));
            AssertRefused(repo, "PinnedTransitive", "leaves Unpinned.Dependency unpinned");
            WriteCentralPackages(root);
            WriteProject(root, "src/PinnedTransitive", PinnedTransitiveProject.Replace("</ItemGroup>", "<PackageReference Include=\"Transitive.B\" /></ItemGroup>", StringComparison.Ordinal));
            AssertRefused(repo, "PinnedTransitive", "before it referenced Transitive.B");
            WriteProject(root, "src/PinnedTransitive", PinnedTransitiveProject.Replace("<ProjectReference Include=\"..\\DefaultIsPackable\\DefaultIsPackable.csproj\" />", string.Empty, StringComparison.Ordinal));
            AssertRefused(repo, "PinnedTransitive", "other projects");
            WriteProject(root, "src/PinnedTransitive", PinnedTransitiveProject);
            File.WriteAllText(Path.Join(root, "src/PinnedTransitive/packages.lock.json"), PinnedTransitiveLockFile.Replace("\"version\": 2", "\"version\": 1", StringComparison.Ordinal));
            AssertRefused(repo, "PinnedTransitive", "not a version 2 lock file");

            Console.WriteLine("Project facts contract cases passed.");
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup.
            }
        }
    }

    private static void WriteFixture(string root)
    {
        File.WriteAllText(Path.Join(root, "Elsa.Server.slnx"), "<Solution></Solution>");
        File.WriteAllText(Path.Join(root, "VersionLines.props"),
            "<Project><PropertyGroup><ElsaVersionLineAMembers>;Fixture.LineA.Core;</ElsaVersionLineAMembers></PropertyGroup></Project>");

        WriteProject(root, "src/ProjectFileOverridesInheritedFalse",
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><IsPackable>true</IsPackable></PropertyGroup></Project>");
        File.WriteAllText(Path.Join(root, "src/ProjectFileOverridesInheritedFalse/Directory.Build.props"),
            "<Project><PropertyGroup><IsPackable>false</IsPackable></PropertyGroup></Project>");

        WriteProject(root, "src/DirectoryBuildPropsOverridesDefault", "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
        File.WriteAllText(Path.Join(root, "src/DirectoryBuildPropsOverridesDefault/Directory.Build.props"),
            "<Project><PropertyGroup><IsPackable>false</IsPackable></PropertyGroup></Project>");

        WriteProject(root, "src/WebSdkDefaultsToFalse", "<Project Sdk=\"Microsoft.NET.Sdk.Web\"></Project>");

        WriteProject(root, "src/TestProjectFlagIsFalse",
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><IsTestProject>true</IsTestProject></PropertyGroup></Project>");

        WriteProject(root, "src/TestSdkReferenceIsFalse",
            "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><PackageReference Include=\"Microsoft.NET.Test.Sdk\" /></ItemGroup></Project>");

        WriteProject(root, "src/DefaultIsPackable", "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");

        WriteProject(root, "src/PackageIdWins",
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><PackageId>Custom.Package.Id</PackageId><AssemblyName>Ignored.Assembly.Name</AssemblyName></PropertyGroup></Project>");
        WriteProject(root, "src/AssemblyNameWins",
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><AssemblyName>Custom.Assembly.Name</AssemblyName></PropertyGroup></Project>");
        WriteProject(root, "src/ProjectNameWins", "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");

        WriteProject(root, "src/Fixture.LineA.Core", "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");
        WriteProject(root, "src/Fixture.LineA", "<Project Sdk=\"Microsoft.NET.Sdk\"></Project>");

        WriteCentralPackages(root);
        WriteProject(root, "src/PinnedTransitive", PinnedTransitiveProject);
        File.WriteAllText(Path.Join(root, "src/PinnedTransitive/packages.lock.json"), PinnedTransitiveLockFile);
    }

    /// <summary>
    /// References one package and one project, which reach two pins between them, and one package privately, which alone
    /// reaches a third.
    /// </summary>
    private const string PinnedTransitiveProject =
        "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><PackageReference Include=\"Direct.Package\" />" +
        "<PackageReference Include=\"Private.Package\" PrivateAssets=\"all\" />" +
        "<ProjectReference Include=\"..\\DefaultIsPackable\\DefaultIsPackable.csproj\" /></ItemGroup></Project>";

    /// <summary>
    /// The lock file of <see cref="PinnedTransitiveProject"/>, in the shape NuGet writes: every pinned package the project
    /// reaches but does not reference is CentralTransitive, the privately reached one too, and a runtime-specific target
    /// lists only what that runtime adds.
    /// </summary>
    private const string PinnedTransitiveLockFile =
        """
        {
          "version": 2,
          "dependencies": {
            "net10.0": {
              "Direct.Package": { "type": "Direct", "requested": "[3.0.0, )", "resolved": "3.0.0", "contentHash": "a", "dependencies": { "Transitive.A": "1.0.0", "Unpinned.Dependency": "1.0.0" } },
              "Private.Package": { "type": "Direct", "requested": "[5.0.0, )", "resolved": "5.0.0", "contentHash": "b", "dependencies": { "Private.Pin": "6.0.0", "Transitive.A": "1.0.0" } },
              "Unpinned.Dependency": { "type": "Transitive", "resolved": "1.0.0", "contentHash": "c" },
              "defaultispackable": { "type": "Project", "dependencies": { "Transitive.B": "[2.0.0, )" } },
              "Private.Pin": { "type": "CentralTransitive", "requested": "[6.0.0, )", "resolved": "6.0.0", "contentHash": "d" },
              "Transitive.A": { "type": "CentralTransitive", "requested": "[1.0.0, )", "resolved": "1.0.0", "contentHash": "e" },
              "Transitive.B": { "type": "CentralTransitive", "requested": "[2.0.0, )", "resolved": "2.0.0", "contentHash": "f" }
            },
            "net10.0/linux-x64": {
              "Runtime.Pin": { "type": "CentralTransitive", "requested": "[7.0.0, )", "resolved": "7.0.0", "contentHash": "g" }
            }
          }
        }
        """;

    private static readonly (string Id, string Version)[] CentralPins =
    [
        ("Direct.Package", "3.0.0"), ("Private.Package", "5.0.0"), ("Private.Pin", "6.0.0"), ("Runtime.Pin", "7.0.0"),
        ("Transitive.A", "1.0.0"), ("Transitive.B", "2.0.0"), ("Unreached.Pin", "4.0.0")
    ];

    /// <summary>Writes <see cref="CentralPins"/>, each of <paramref name="changes"/> replacing a pin of its id or adding one.</summary>
    private static void WriteCentralPackages(string root, params (string Id, string Version)[] changes) =>
        File.WriteAllText(Path.Join(root, "Directory.Packages.props"),
            "<Project><ItemGroup>" +
            string.Concat(CentralPins.Where(pin => changes.All(change => change.Id != pin.Id)).Concat(changes)
                .Select(pin => $"<PackageVersion Include=\"{pin.Id}\" Version=\"{pin.Version}\" />")) +
            "</ItemGroup></Project>");

    private static void AssertPackable(IReadOnlyDictionary<string, ProjectFacts> facts, string project, bool expected, string message)
    {
        if (facts[project].Packable != expected)
            throw new InvalidOperationException($"{project}: expected Packable={expected}, got {facts[project].Packable}. {message}");
    }

    private static void AssertPackageId(IReadOnlyDictionary<string, ProjectFacts> facts, string project, string expected)
    {
        if (facts[project].PackageId != expected)
            throw new InvalidOperationException($"{project}: expected PackageId='{expected}', got '{facts[project].PackageId}'.");
    }

    private static void AssertLine(IReadOnlyDictionary<string, ProjectFacts> facts, string project, string expected)
    {
        if (facts[project].Line != expected)
            throw new InvalidOperationException($"{project}: expected Line='{expected}', got '{facts[project].Line}'.");
    }

    /// <summary>The project's pinned-transitive edges, as the dependency map records them, read for it alone.</summary>
    private static IReadOnlyList<PinnedTransitiveEdge> PinnedTransitive(RepoContext repo, string project)
    {
        var graph = ProjectGraph.Read(repo);
        return PinnedTransitiveDependencies.Read(repo, graph.Single(fact => fact.Name == project), graph, PackageVersions.Load(repo));
    }

    private static void AssertPinnedTransitive(RepoContext repo, string project, string expected)
    {
        var actual = string.Join(", ", PinnedTransitive(repo, project).Select(edge => $"{edge.Id} {edge.Version}"));
        if (actual != expected)
            throw new InvalidOperationException($"{project}: expected pinned-transitive edges '{expected}', got '{actual}'.");
    }

    private static void AssertRefused(RepoContext repo, string project, string reason)
    {
        try
        {
            PinnedTransitive(repo, project);
        }
        catch (InvalidOperationException exception) when (exception.Message.Contains(reason, StringComparison.Ordinal) && exception.Message.Contains("dotnet restore", StringComparison.Ordinal))
        {
            return;
        }

        throw new InvalidOperationException($"{project}: expected its lock file to be refused ({reason}), but it was read.");
    }
}
