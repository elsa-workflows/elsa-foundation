using Xunit;
using static Elsa.Architecture.Tests.RepoPaths;

namespace Elsa.Architecture.Tests;

/// <summary>
/// Proves <c>PackageRanges.targets</c>' <c>ElsaVerifyNuspecRanges</c> directly, against a nuspec it writes itself
/// rather than one a real pack produces: <see cref="PackageVersioningPackTests"/> proves a real pack bounds a project
/// reference's range and leaves a third party's alone, but every other check <c>PackageVersioning.props</c> runs
/// refuses a project reference whose version is not what the computation - or the dev fallback - says it should be,
/// so a real pack can never manufacture an unbounded range on one of this repository's own packages to prove
/// <c>ElsaVerifyNuspecRanges</c> catches one. This fixture writes that nuspec by hand instead, redefining the stub
/// <c>GenerateNuspec</c> target <see cref="MsBuildFixture"/> defines, so <c>ElsaVerifyNuspecRanges</c> - wired to run
/// <c>AfterTargets="GenerateNuspec"</c> regardless of what that target does - reads it the same way it would read one
/// pack wrote.
/// </summary>
public sealed class PackageRangesBuildCheckTests : IDisposable
{
    private const string Commit = "0123456789abcdef0123456789abcdef01234567";

    private readonly DirectoryInfo scratch = Directory.CreateTempSubdirectory(nameof(PackageRangesBuildCheckTests));
    private readonly string root;

    public PackageRangesBuildCheckTests() => root = MsBuildFixture.CopyRootBuildFiles(scratch.FullName);

    /// <summary>
    /// A nuspec dependency on <c>Fixture.Lib</c>, a project reference, whose range crosses two majors - not bounded at
    /// its floor's next major - fails the pack and deletes the nupkg; the same range on a package that is nobody's
    /// project reference, a stand-in for a third party, is left alone.
    /// </summary>
    [Fact]
    public void An_unbounded_range_on_a_project_reference_fails_while_a_third_partys_passes()
    {
        var (exitCode, output) = MsBuildFixture.Run(
            "Fixture.LineB", Body(ownRange: "[4.0.3-preview, 6.0.0)", thirdPartyRange: "[10.0.10, 11.0.0)"),
            repositoryRoot: root, target: "GenerateNuspec", importFirst: Computed());

        Assert.True(exitCode != 0, $"expected a build failure but it succeeded:\n{output}");
        Assert.Contains("ELSAPV008", output);
        Assert.Contains("Fixture.Lib", output);
        Assert.DoesNotContain("Third.Party", output);
    }

    /// <summary>A project reference's range bounded at its floor's next major, and any range on a third party, pass.</summary>
    [Fact]
    public void A_bounded_range_on_a_project_reference_and_any_range_on_a_third_party_pass()
    {
        var (exitCode, output) = MsBuildFixture.Run(
            "Fixture.LineB", Body(ownRange: "[4.0.3-preview, 5.0.0)", thirdPartyRange: "[10.0.10, )"),
            repositoryRoot: root, target: "GenerateNuspec", importFirst: Computed());

        Assert.True(exitCode == 0, $"build failed with exit {exitCode}:\n{output}");
    }

    public void Dispose() => scratch.Delete(recursive: true);

    /// <summary>
    /// Runs ahead of the stub <c>GenerateNuspec</c> <see cref="MsBuildFixture"/> writes, producing a nupkg and a nuspec
    /// beside it, naming a project reference to <c>Fixture.Lib</c> - so <c>ElsaVerifyNuspecRanges</c> reads it as one
    /// of this repository's own - and a dependency on <c>Third.Party</c>, which is nobody's project reference here.
    /// </summary>
    private static string Body(string ownRange, string thirdPartyRange) => $"""
        <PropertyGroup>
          <IsPackable>true</IsPackable>
          <!-- Stands in for what SourceLink would set from a real git checkout; ElsaVerifyRepositoryCommit compares it. -->
          <SourceRevisionId>{Commit}</SourceRevisionId>
        </PropertyGroup>
        <ItemGroup>
          <ProjectReference Include="../Lib/Fixture.Lib.csproj" />
        </ItemGroup>
        <Target Name="WriteFakeNuspec" BeforeTargets="GenerateNuspec">
          <WriteLinesToFile File="$(MSBuildProjectDirectory)/out.nuspec" Overwrite="true" Lines="&lt;package&gt;&lt;metadata&gt;&lt;dependencies&gt;&lt;group&gt;&lt;dependency id=&quot;Fixture.Lib&quot; version=&quot;{ownRange}&quot; /&gt;&lt;dependency id=&quot;Third.Party&quot; version=&quot;{thirdPartyRange}&quot; /&gt;&lt;/group&gt;&lt;/dependencies&gt;&lt;/metadata&gt;&lt;/package&gt;" />
          <WriteLinesToFile File="$(MSBuildProjectDirectory)/out.nupkg" Overwrite="true" Lines="nupkg" />
          <ItemGroup>
            <_OutputPackItems Include="$(MSBuildProjectDirectory)/out.nuspec" />
            <_OutputPackItems Include="$(MSBuildProjectDirectory)/out.nupkg" />
          </ItemGroup>
        </Target>
        <Target Name="_GetOutputItemsFromPack" />
        """;

    /// <summary>A pack-properties file, shaped as the calculator writes it, for <see cref="Commit"/>.</summary>
    private string Computed()
    {
        var path = Path.Join(scratch.FullName, "package-versions.props");
        File.WriteAllText(path, $"""
            <Project>
              <PropertyGroup>
                <ElsaVersionComputationCommit>{Commit}</ElsaVersionComputationCommit>
              </PropertyGroup>
              <PropertyGroup Condition="'$(MSBuildProjectName)' == 'Fixture.LineB'">
                <ElsaComputedPackageVersion>4.0.8-preview</ElsaComputedPackageVersion>
                <ElsaInputFingerprint>sha256:{new string('a', 64)}</ElsaInputFingerprint>
              </PropertyGroup>
            </Project>
            """);
        return path;
    }
}
