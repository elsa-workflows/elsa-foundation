using System.Diagnostics;
using Xunit;

namespace Elsa.Architecture.Tests;

/// <summary>
/// Proves the build-time half of ADR 0067's version-line guard: the <c>ElsaVerifyVersionLine</c> target in
/// the root <c>Directory.Build.props</c>, which runs on both <c>BeforeBuild</c> and <c>GenerateNuspec</c> so
/// a pack-only override is caught too. <see cref="VersionLineOwnershipGuardTests"/> is a static file scan
/// that cannot see a global property passed on the command line (<c>/p:ElsaVersionLine=...</c> or
/// <c>/p:ElsaVersionLineAMembers=...</c>) or a setter that only takes effect at evaluation time from a build
/// file outside <c>src/</c> and <c>tests/</c>; this suite spawns real <c>dotnet msbuild</c> processes against
/// small fixture projects that import the real root <c>Directory.Build.props</c> (or, for one case, a copy
/// of it paired with a deliberately broken <c>VersionLines.props</c>), so it exercises the actual
/// <c>BeforeTargets</c> wiring, the actual <c>/p:</c> handling for both properties, and the actual
/// <c>XmlPeek</c> comparison — including the case where <c>XmlPeek</c> itself reads nothing
/// (<c>ELSAVL003</c>) — rather than a description of them.
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
        string projectName, string body, string[] globalProperties, string expectedCode, string? expectedValue)
    {
        var (exitCode, output) = RunBuild(projectName, body, globalProperties);

        Assert.True(exitCode != 0, $"expected a build failure but it succeeded:\n{output}");
        Assert.Contains(expectedCode, output);
        if (expectedValue is not null)
            Assert.Contains(expectedValue, output);
    }

    public static IEnumerable<object?[]> MismatchCases()
    {
        var lineAMember = VersionLines.LineAMembers(RepoRoot)[0];

        // A project file sets ElsaVersionLine directly, disagreeing with the derived line -> ELSAVL001.
        yield return new object?[]
        {
            "Fixture.LineB",
            "<PropertyGroup><ElsaVersionLine>A</ElsaVersionLine></PropertyGroup>",
            Array.Empty<string>(),
            "ELSAVL001",
            null
        };

        // A project file replaces the reviewed list itself, leaving ElsaVersionLine untouched -> ELSAVL002.
        yield return new object?[]
        {
            "Fixture.LineB",
            "<PropertyGroup><ElsaVersionLineAMembers>;Fixture.LineB;</ElsaVersionLineAMembers></PropertyGroup>",
            Array.Empty<string>(),
            "ELSAVL002",
            null
        };

        // /p:ElsaVersionLine wins over the props-file assignment for a Line B project -> ELSAVL001.
        yield return new object?[]
        {
            "Fixture.LineB",
            "",
            new[] { "ElsaVersionLine=A" },
            "ELSAVL001",
            null
        };

        // /p:ElsaVersionLine wins over the props-file assignment for a real Line A member -> ELSAVL001.
        yield return new object?[]
        {
            lineAMember,
            "",
            new[] { "ElsaVersionLine=B" },
            "ELSAVL001",
            null
        };

        // /p:ElsaVersionLineAMembers wins over the props-file assignment too -> ELSAVL002. ';' separates
        // properties on the msbuild command line, so the literal ';' delimiters must be passed as %3B; the
        // error message must still show the real value, ';Fixture.LineB;', not the encoded one.
        yield return new object?[]
        {
            "Fixture.LineB",
            "",
            new[] { "ElsaVersionLineAMembers=%3BFixture.LineB%3B" },
            "ELSAVL002",
            ";Fixture.LineB;"
        };
    }

    /// <summary>
    /// Without a namespace-qualified <c>VersionLines.props</c>, MSBuild still imports the file just fine, but
    /// the namespace-less <c>XmlPeek</c> query in <c>ElsaVerifyVersionLine</c> then matches nothing against a
    /// namespaced document, so <c>_ElsaReviewedLineAMembers</c> comes back empty and the target raises
    /// <c>ELSAVL003</c>. This is what keeps <c>ELSAVL003</c> honest: without it, deleting that <c>Error</c>
    /// would silently turn the whole check off the day <c>VersionLines.props</c> changes shape, because
    /// <c>ELSAVL001</c>/<c>ELSAVL002</c> only fire once the list was actually read.
    /// </summary>
    [Fact]
    public void Xmlpeek_reading_nothing_fails_with_ELSAVL003()
    {
        var directory = Directory.CreateTempSubdirectory(nameof(VersionLineBuildCheckTests));
        try
        {
            var directoryBuildPropsPath = Path.Join(directory.FullName, "Directory.Build.props");
            File.Copy(Path.Join(RepoRoot, "Directory.Build.props"), directoryBuildPropsPath);

            // The real list, verbatim, with only the root <Project> element namespaced.
            var versionLines = File.ReadAllText(Path.Join(RepoRoot, "VersionLines.props"))
                .Replace("<Project>", "<Project xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\">");
            File.WriteAllText(Path.Join(directory.FullName, "VersionLines.props"), versionLines);

            var (exitCode, output) = RunBuild("Fixture.LineB", directoryBuildPropsPath: directoryBuildPropsPath);

            Assert.True(exitCode != 0, $"expected a build failure but it succeeded:\n{output}");
            Assert.Contains("ELSAVL003", output);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    /// <summary>
    /// Writes a minimal, SDK-less fixture project (no restore needed, about a second per run) that imports
    /// <paramref name="directoryBuildPropsPath"/> — the real root <c>Directory.Build.props</c> by default, or
    /// a caller-supplied copy for <see cref="Xmlpeek_reading_nothing_fails_with_ELSAVL003"/> — and runs
    /// <c>-t:BeforeBuild</c> against it, proving the <c>BeforeTargets</c> wiring itself, not just the target's
    /// own body in isolation.
    /// </summary>
    private static (int ExitCode, string Output) RunBuild(
        string projectName, string body = "", string[]? globalProperties = null, string? directoryBuildPropsPath = null)
    {
        var directory = Directory.CreateTempSubdirectory(nameof(VersionLineBuildCheckTests));
        try
        {
            var projectPath = Path.Join(directory.FullName, $"{projectName}.proj");
            File.WriteAllText(projectPath, $"""
                <Project>
                  <Import Project="{directoryBuildPropsPath ?? Path.Join(RepoRoot, "Directory.Build.props")}" />
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
            foreach (var property in globalProperties ?? [])
                startInfo.ArgumentList.Add($"-p:{property}");

            // dotnet test's own build/vstest launch leaves MSBuild-specific variables (MSBUILD_EXE_PATH,
            // MSBuildExtensionsPath, MSBuildSDKsPath) in this process's environment, pointing at the test
            // host's build context. Inherited by the child process, they make the spawned `dotnet msbuild`
            // resolve the wrong SDK/props set instead of its own, which breaks evaluating the fixture
            // project outright rather than merely producing a different — still checkable — result.
            startInfo.Environment.Remove("MSBUILD_EXE_PATH");
            startInfo.Environment.Remove("MSBuildExtensionsPath");
            startInfo.Environment.Remove("MSBuildSDKsPath");

            using var process = Process.Start(startInfo)!;

            // Start both reads before blocking on exit: stdout and stderr are separate pipes with bounded
            // buffers, so reading them sequentially can deadlock if the child fills one while this process
            // is still blocked reading the other.
            var stdOutTask = process.StandardOutput.ReadToEndAsync();
            var stdErrTask = process.StandardError.ReadToEndAsync();

            var exited = process.WaitForExit(TimeSpan.FromMinutes(2));
            if (!exited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit();
            }

            var output = stdOutTask.GetAwaiter().GetResult() + stdErrTask.GetAwaiter().GetResult();

            if (!exited)
                throw new TimeoutException(
                    $"dotnet msbuild for {projectName} did not exit within 2 minutes and was killed. Output so far:\n{output}");

            return (process.ExitCode, output);
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
