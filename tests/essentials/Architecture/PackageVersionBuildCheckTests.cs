using Xunit;
using static Elsa.Architecture.Tests.RepoPaths;

namespace Elsa.Architecture.Tests;

/// <summary>
/// Proves how <c>PackageVersioning.props</c> chooses each project's version, and the checks it runs on every build and
/// pack (spec 150, #2080), against small SDK-less fixture projects under <c>src/</c> of a copy of the root build files.
/// Computed input is the calculator's pack-properties file, which the fixture imports ahead of
/// <c>Directory.Build.props</c> as MSBuild's <c>CustomBeforeDirectoryBuildProps</c> hook does;
/// <see cref="PackageVersioningPackTests"/> proves the hook itself, with real packs.
/// </summary>
public sealed class PackageVersionBuildCheckTests : IDisposable
{
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";

    /// <summary>Prints the versions the fixture evaluated, after every check has run.</summary>
    private const string ShowVersion =
        """<Target Name="ShowVersion" AfterTargets="BeforeBuild"><Message Importance="high" Text="PackageVersion=$(PackageVersion) Version=$(Version) RepositoryCommit=$(RepositoryCommit)." /></Target>""";

    private readonly DirectoryInfo scratch = Directory.CreateTempSubdirectory(nameof(PackageVersionBuildCheckTests));
    private readonly string root;

    public PackageVersionBuildCheckTests() => root = MsBuildFixture.CopyRootBuildFiles(scratch.FullName);

    /// <summary>
    /// Without computed input a package is its line's dev version, and the assembly version is left to the SDK, so a dev
    /// build's assemblies - and every activity version and assembly-qualified name derived from them - do not change.
    /// </summary>
    [Fact]
    public void Without_computed_input_a_package_is_its_lines_dev_version()
    {
        var (exitCode, output) = MsBuildFixture.Run("Fixture.LineB", ShowVersion, repositoryRoot: root);

        Assert.True(exitCode == 0, $"build failed with exit {exitCode}:\n{output}");
        Assert.Contains("PackageVersion=4.0.0-dev Version= RepositoryCommit=.", output);
    }

    /// <summary>
    /// FR-005 covers <c>src/</c>, where every published package lives. A project elsewhere - a test fixture pinning an
    /// assembly version, a sample package - is left exactly as its own properties have it.
    /// </summary>
    [Fact]
    public void A_project_outside_src_keeps_its_own_version()
    {
        var (exitCode, output) = MsBuildFixture.Run("Fixture.LineB", "<PropertyGroup><Version>2.1.0</Version></PropertyGroup>" + ShowVersion);

        Assert.True(exitCode == 0, $"build failed with exit {exitCode}:\n{output}");
        Assert.Contains("PackageVersion= Version=2.1.0 ", output);
    }

    /// <summary>
    /// MSBuild consumes the same line properties the calculator reads: a Line A project takes ElsaContractsVersion and a
    /// Line B project ElsaVersion. Both are 4.0 today, so a copy of the root build files with the lines apart tells them
    /// apart.
    /// </summary>
    [Theory]
    [InlineData(false, "4.1.0-dev")]
    [InlineData(true, "4.3.0-dev")]
    public void The_dev_version_comes_from_the_projects_own_line(bool lineA, string expected)
    {
        var linesApart = MsBuildFixture.CopyRootBuildFiles(scratch.CreateSubdirectory("lines-apart").FullName, versionLines => versionLines
            .Replace("<ElsaVersion>4.0</ElsaVersion>", "<ElsaVersion>4.1</ElsaVersion>", StringComparison.Ordinal)
            .Replace("<ElsaContractsVersion>4.0</ElsaContractsVersion>", "<ElsaContractsVersion>4.3</ElsaContractsVersion>", StringComparison.Ordinal));

        var (exitCode, output) = MsBuildFixture.Run(
            lineA ? VersionLines.LineAMembers(RepoRoot)[0] : "Fixture.LineB", ShowVersion, repositoryRoot: linesApart);

        Assert.True(exitCode == 0, $"build failed with exit {exitCode}:\n{output}");
        Assert.Contains($"PackageVersion={expected} ", output);
    }

    /// <summary>Computed input sets the package version and the assembly version alike, and the nuspec's source commit.</summary>
    [Fact]
    public void Computed_input_sets_the_versions_and_the_repository_commit()
    {
        var (exitCode, output) = MsBuildFixture.Run(
            "Fixture.LineB", ShowVersion, repositoryRoot: root, importFirst: Computed(("Fixture.LineB", "4.0.8-preview")));

        Assert.True(exitCode == 0, $"build failed with exit {exitCode}:\n{output}");
        Assert.Contains($"PackageVersion=4.0.8-preview Version=4.0.8-preview RepositoryCommit={Commit}.", output);
    }

    /// <summary>
    /// packages.yml still stamps one /p:Version on every package until #2082 rewrites it, so without computed input a
    /// global Version becomes the package version; the dev version never overrides it.
    /// </summary>
    [Fact]
    public void Without_computed_input_a_global_version_is_the_package_version()
    {
        var (exitCode, output) = MsBuildFixture.Run("Fixture.LineB", ShowVersion, ["Version=4.0.0-preview.123"], root);

        Assert.True(exitCode == 0, $"build failed with exit {exitCode}:\n{output}");
        Assert.Contains("PackageVersion=4.0.0-preview.123 Version=4.0.0-preview.123 ", output);
    }

    public static TheoryData<string, string[], bool, string> Overrides => new()
    {
        // With computed input, a project's own Version, or a global /p:Version, would stamp another version on its assemblies.
        { "<PropertyGroup><Version>9.9.9</Version></PropertyGroup>", [], true, "ELSAPV003" },
        { "", ["Version=4.0.0-preview.123"], true, "ELSAPV003" },
        // Nothing sets PackageVersion, with or without computed input.
        { "", ["PackageVersion=1.2.3"], false, "ELSAPV004" },
        { "<PropertyGroup><PackageVersion>1.2.3</PackageVersion></PropertyGroup>", [], false, "ELSAPV004" },
        { "<PropertyGroup><PackageVersion>1.2.3</PackageVersion></PropertyGroup>", [], true, "ELSAPV004" },
    };

    [Theory]
    [MemberData(nameof(Overrides))]
    public void An_override_of_the_version_fails(string body, string[] globalProperties, bool computed, string expectedCode)
    {
        var (exitCode, output) = MsBuildFixture.Run(
            "Fixture.LineB", body, globalProperties, root, importFirst: computed ? Computed(("Fixture.LineB", "4.0.8-preview")) : null);

        Assert.True(exitCode != 0, $"expected a build failure but it succeeded:\n{output}");
        Assert.Contains(expectedCode, output);
    }

    /// <summary>A computation made for a commit whose lines differ from this one's is refused, not packed.</summary>
    [Fact]
    public void A_computed_version_off_the_projects_line_fails()
    {
        var (exitCode, output) = MsBuildFixture.Run("Fixture.LineB", repositoryRoot: root, importFirst: Computed(("Fixture.LineB", "4.1.0-preview")));

        Assert.True(exitCode != 0, $"expected a build failure but it succeeded:\n{output}");
        Assert.Contains("ELSAPV002", output);
    }

    /// <summary>Computed values that arrive without the commit they were computed for are refused.</summary>
    [Fact]
    public void Computed_values_without_their_commit_fail()
    {
        var (exitCode, output) = MsBuildFixture.Run(
            "Fixture.LineB", "<PropertyGroup><ElsaInputFingerprint>sha256:00</ElsaInputFingerprint></PropertyGroup>", repositoryRoot: root);

        Assert.True(exitCode != 0, $"expected a build failure but it succeeded:\n{output}");
        Assert.Contains("ELSAPV001", output);
    }

    /// <summary>
    /// A packable project the computation leaves out would fall back to the dev version. Building it is harmless, but
    /// packing it, or answering a referencing pack's <c>_GetProjectVersion</c>, would publish that dev version or a range
    /// starting at it.
    /// </summary>
    [Theory]
    [InlineData("BeforeBuild", false)]
    [InlineData("GenerateNuspec", true)]
    [InlineData("_GetProjectVersion", true)]
    public void A_packable_project_the_computation_leaves_out_cannot_be_packed(string target, bool fails)
    {
        var (exitCode, output) = MsBuildFixture.Run(
            "Fixture.LineB", "<PropertyGroup><IsPackable>true</IsPackable></PropertyGroup>", repositoryRoot: root, target: target,
            importFirst: Computed(("Fixture.Other", "4.0.8-preview")));

        Assert.True(fails == (exitCode != 0), $"exit {exitCode}:\n{output}");
        Assert.Equal(fails, output.Contains("ELSAPV005", StringComparison.Ordinal));
    }

    public void Dispose() => scratch.Delete(recursive: true);

    /// <summary>A pack-properties file, shaped as the calculator writes it, for <see cref="Commit"/>.</summary>
    private string Computed(params (string Project, string Version)[] packages)
    {
        var path = Path.Join(scratch.FullName, "package-versions.props");
        File.WriteAllText(path, $"""
            <Project>
              <PropertyGroup>
                <ElsaVersionComputationCommit>{Commit}</ElsaVersionComputationCommit>
              </PropertyGroup>
            {string.Concat(packages.Select(package => $"""
              <PropertyGroup Condition="'$(MSBuildProjectName)' == '{package.Project}'">
                <ElsaComputedPackageVersion>{package.Version}</ElsaComputedPackageVersion>
                <ElsaInputFingerprint>sha256:{new string('a', 64)}</ElsaInputFingerprint>
              </PropertyGroup>

            """))}</Project>
            """);
        return path;
    }
}
