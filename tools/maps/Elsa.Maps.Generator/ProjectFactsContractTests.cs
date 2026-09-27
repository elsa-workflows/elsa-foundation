using System.Diagnostics;

namespace Elsa.Maps.Generator;

/// <summary>
/// Dependency-free contract tests for the project graph's packable precedence, package-id resolution,
/// Line A/B whole-name matching, and the reading of pinned-transitive edges from restore output. Keeps
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

            // Pinned-transitive edges (spec 149 FR-012) come from restore output, restored here under another checkout
            // path, and only once it is held to the tree: a restore older than a pin, a reference or the project itself
            // is refused rather than read, since a stale list would leave a package's nuspec change undetected.
            AssertPinnedTransitive(repo, "PinnedTransitive", "Transitive.A 1.0.0, Transitive.B 2.0.0");
            AssertRefused(repo, "DefaultIsPackable", "has no restore output");
            WriteCentralPackages(root, transitiveAVersion: "1.0.1");
            AssertRefused(repo, "PinnedTransitive", "other pins");
            WriteCentralPackages(root, transitiveAVersion: "1.0.0");
            WriteProject(root, "src/PinnedTransitive", PinnedTransitiveProject.Replace("</ItemGroup>", "<PackageReference Include=\"Transitive.B\" /></ItemGroup>", StringComparison.Ordinal));
            AssertRefused(repo, "PinnedTransitive", "before it referenced Transitive.B");
            WriteProject(root, "src/PinnedTransitive", PinnedTransitiveProject.Replace("<ProjectReference Include=\"..\\DefaultIsPackable\\DefaultIsPackable.csproj\" />", string.Empty, StringComparison.Ordinal));
            AssertRefused(repo, "PinnedTransitive", "other project references");

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

        WriteCentralPackages(root, transitiveAVersion: "1.0.0");
        WriteProject(root, "src/PinnedTransitive", PinnedTransitiveProject);
        Directory.CreateDirectory(Path.Join(root, "src/PinnedTransitive/obj"));
        File.WriteAllText(Path.Join(root, "src/PinnedTransitive/obj/project.assets.json"), PinnedTransitiveAssets);
    }

    /// <summary>References one package directly and one project, and reaches two pins only through them.</summary>
    private const string PinnedTransitiveProject =
        "<Project Sdk=\"Microsoft.NET.Sdk\"><ItemGroup><PackageReference Include=\"Direct.Package\" />" +
        "<ProjectReference Include=\"..\\DefaultIsPackable\\DefaultIsPackable.csproj\" /></ItemGroup></Project>";

    /// <summary>The restore output of <see cref="PinnedTransitiveProject"/>, in the shape NuGet writes, from another checkout path.</summary>
    private const string PinnedTransitiveAssets =
        """
        {
          "version": 3,
          "centralTransitiveDependencyGroups": {
            "net10.0": {
              "Transitive.B": { "include": "Runtime, Compile, Native, BuildTransitive", "version": "[2.0.0, )" },
              "Transitive.A": { "include": "Runtime, Compile, Native, BuildTransitive", "version": "[1.0.0, )" }
            }
          },
          "project": {
            "restore": {
              "projectPath": "/elsewhere/checkout/src/PinnedTransitive/PinnedTransitive.csproj",
              "frameworks": {
                "net10.0": {
                  "projectReferences": {
                    "/elsewhere/checkout/src/DefaultIsPackable/DefaultIsPackable.csproj": { "projectPath": "/elsewhere/checkout/src/DefaultIsPackable/DefaultIsPackable.csproj" }
                  }
                }
              }
            },
            "frameworks": {
              "net10.0": {
                "dependencies": { "Direct.Package": { "target": "Package", "version": "[3.0.0, )", "versionCentrallyManaged": true } },
                "centralPackageVersions": { "Direct.Package": "3.0.0", "Transitive.A": "1.0.0", "Transitive.B": "2.0.0", "Unreached.Pin": "4.0.0" }
              }
            }
          }
        }
        """;

    private static void WriteCentralPackages(string root, string transitiveAVersion) =>
        File.WriteAllText(Path.Join(root, "Directory.Packages.props"),
            "<Project><ItemGroup>" +
            "<PackageVersion Include=\"Direct.Package\" Version=\"3.0.0\" />" +
            $"<PackageVersion Include=\"Transitive.A\" Version=\"{transitiveAVersion}\" />" +
            "<PackageVersion Include=\"Transitive.B\" Version=\"2.0.0\" />" +
            "<PackageVersion Include=\"Unreached.Pin\" Version=\"4.0.0\" />" +
            "</ItemGroup></Project>");

    private static void WriteProject(string root, string relativeDirectory, string contents)
    {
        var directory = Path.Join(root, relativeDirectory);
        Directory.CreateDirectory(directory);
        var name = relativeDirectory[(relativeDirectory.LastIndexOf('/') + 1)..];
        File.WriteAllText(Path.Join(directory, $"{name}.csproj"), contents);
    }

    private static void GitInit(string root)
    {
        RunGit(root, "init", "-q");
        RunGit(root, "config", "user.email", "test@example.com");
        RunGit(root, "config", "user.name", "Test");
    }

    private static void RunGit(string root, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git") { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start git.");
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {process.StandardError.ReadToEnd()}");
    }

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
    private static IReadOnlyList<PinnedTransitiveEdge> PinnedTransitive(RepoContext repo, string project) =>
        PinnedTransitiveDependencies.Attach(repo, [.. ProjectGraph.Read(repo).Where(fact => fact.Name == project)])
            .Single().Edges.OfType<PinnedTransitiveEdge>().ToArray();

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

        throw new InvalidOperationException($"{project}: expected its restore output to be refused ({reason}), but it was read.");
    }
}
