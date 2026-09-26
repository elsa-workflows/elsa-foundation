using System.Diagnostics;

namespace Elsa.Maps.Generator;

/// <summary>
/// Dependency-free contract tests for the project graph's packable precedence, package-id resolution
/// and Line A/B whole-name matching. Keeps the precedence order and the whole-name semantics pinned
/// against fixtures rather than only against whatever the live tree happens to contain.
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
    }

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
}
