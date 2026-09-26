using System.Diagnostics;
using Xunit;

namespace Elsa.Architecture.Tests;

/// <summary>
/// Proves the build-time half of ADR 0067's version-line guard: the <c>ElsaVerifyVersionLine</c> target in
/// the root <c>Directory.Build.props</c>. <see cref="VersionLineOwnershipGuardTests"/> is a static file scan
/// that cannot see a global property passed on the command line (<c>/p:ElsaVersionLine=...</c> or
/// <c>/p:ElsaVersionLineAMembers=...</c>) or a build file outside <c>src/</c> and <c>tests/</c>; this suite
/// spawns real <c>dotnet msbuild</c> processes against small fixture projects that import the real root
/// <c>Directory.Build.props</c>, so it exercises the actual <c>BeforeTargets="BeforeBuild"</c> wiring and the
/// actual <c>XmlPeek</c> comparison rather than a description of them.
/// </summary>
public sealed class VersionLineBuildCheckTests
{
    [Fact]
    public void A_clean_line_B_project_builds()
    {
        var (exitCode, output) = RunBuild("Fixture.LineB");

        Assert.True(exitCode == 0, $"build failed with exit {exitCode}:\n{output}");
    }

    /// <summary>Proves the peeked list and the derived line agree for a real Line A member, not just a fixture name.</summary>
    [Fact]
    public void A_clean_line_A_project_builds()
    {
        var (exitCode, output) = RunBuild(VersionLines.LineAMembers(RepoRoot)[0]);

        Assert.True(exitCode == 0, $"build failed with exit {exitCode}:\n{output}");
    }

    [Theory]
    [MemberData(nameof(MismatchCases))]
    public void A_mismatch_fails_with_the_expected_code(
        string projectName, string body, string[] globalProperties, string expectedCode)
    {
        var (exitCode, output) = RunBuild(projectName, body, globalProperties);

        Assert.True(exitCode != 0, $"expected a build failure but it succeeded:\n{output}");
        Assert.Contains(expectedCode, output);
    }

    public static IEnumerable<object[]> MismatchCases()
    {
        var lineAMember = VersionLines.LineAMembers(RepoRoot)[0];

        // A project file sets ElsaVersionLine directly, disagreeing with the derived line -> ELSAVL001.
        yield return new object[]
        {
            "Fixture.LineB",
            "<PropertyGroup><ElsaVersionLine>A</ElsaVersionLine></PropertyGroup>",
            Array.Empty<string>(),
            "ELSAVL001"
        };

        // A project file replaces the reviewed list itself, leaving ElsaVersionLine untouched -> ELSAVL002.
        yield return new object[]
        {
            "Fixture.LineB",
            "<PropertyGroup><ElsaVersionLineAMembers>;Fixture.LineB;</ElsaVersionLineAMembers></PropertyGroup>",
            Array.Empty<string>(),
            "ELSAVL002"
        };

        // /p:ElsaVersionLine wins over the props-file assignment for a Line B project -> ELSAVL001.
        yield return new object[]
        {
            "Fixture.LineB",
            "",
            new[] { "ElsaVersionLine=A" },
            "ELSAVL001"
        };

        // /p:ElsaVersionLine wins over the props-file assignment for a real Line A member -> ELSAVL001.
        yield return new object[]
        {
            lineAMember,
            "",
            new[] { "ElsaVersionLine=B" },
            "ELSAVL001"
        };
    }

    /// <summary>
    /// Writes a minimal, SDK-less fixture project (no restore needed, about a second per run) that imports
    /// the real root <c>Directory.Build.props</c> and runs <c>-t:BeforeBuild</c> against it -- proving the
    /// <c>BeforeTargets="BeforeBuild"</c> wiring itself, not just the target's own body in isolation.
    /// </summary>
    private static (int ExitCode, string Output) RunBuild(
        string projectName, string body = "", params string[] globalProperties)
    {
        var directory = Directory.CreateTempSubdirectory(nameof(VersionLineBuildCheckTests));
        try
        {
            var projectPath = Path.Join(directory.FullName, $"{projectName}.proj");
            File.WriteAllText(projectPath, $"""
                <Project>
                  <Import Project="{Path.Join(RepoRoot, "Directory.Build.props")}" />
                  {body}
                  <Target Name="BeforeBuild" />
                </Project>
                """);

            var startInfo = new ProcessStartInfo(DotnetMuxer())
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add("msbuild");
            startInfo.ArgumentList.Add(projectPath);
            startInfo.ArgumentList.Add("-t:BeforeBuild");
            startInfo.ArgumentList.Add("-nologo");
            startInfo.ArgumentList.Add("-nodeReuse:false");
            foreach (var property in globalProperties)
                startInfo.ArgumentList.Add($"-p:{property}");

            // dotnet test's own build/vstest launch leaves MSBuild-specific variables (MSBUILD_EXE_PATH,
            // MSBuildExtensionsPath, MSBuildSDKsPath) in this process's environment, pointing at the test
            // host's build context. Inherited by the child process, they make the spawned `dotnet msbuild`
            // resolve the wrong SDK/props set instead of its own, which breaks evaluating the fixture
            // project outright rather than merely producing a different -- still checkable -- result.
            startInfo.Environment.Remove("MSBUILD_EXE_PATH");
            startInfo.Environment.Remove("MSBuildExtensionsPath");
            startInfo.Environment.Remove("MSBuildSDKsPath");

            using var process = Process.Start(startInfo)!;
            var output = process.StandardOutput.ReadToEnd();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return (process.ExitCode, output + error);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static string DotnetMuxer() =>
        Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") is { Length: > 0 } path ? path : "dotnet";

    private static string RepoRoot { get; } = FindRepoRoot();

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Join(directory.FullName, "Elsa.Server.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate the repository root.");
    }
}
