using Xunit;
using static Elsa.Architecture.Tests.RepoPaths;

namespace Elsa.Architecture.Tests;

/// <summary>
/// Proves the build-time half of ADR 0067's version-line guard: the <c>ElsaVerifyVersionLine</c> target in
/// the root <c>Directory.Build.props</c>, which runs on <c>BeforeBuild</c>, <c>GenerateNuspec</c> and
/// <c>_GetProjectVersion</c> so a pack-only override is caught too. <see cref="VersionLineOwnershipGuardTests"/> and
/// <see cref="NoLiteralProjectVersionGuardTests"/> are static file scans that cannot see a global property passed on
/// the command line (<c>/p:ElsaVersionLine=...</c>, <c>/p:ElsaVersion=...</c> and the like) or a setter that only
/// takes effect at evaluation time from a build file they do not scan; this suite spawns real
/// <c>dotnet msbuild</c> processes against small fixture projects that import the real root
/// <c>Directory.Build.props</c> (or, for one case, a copy of it paired with a deliberately broken
/// <c>VersionLines.props</c>), so it exercises the actual <c>BeforeTargets</c> wiring for each hook, the actual
/// <c>/p:</c> handling for each property, and the actual <c>XmlPeek</c> comparison — including the case where
/// <c>XmlPeek</c> itself reads nothing (<c>ELSAVL003</c>) — rather than a description of them.
/// </summary>
public sealed class VersionLineBuildCheckTests
{
    [Fact]
    public void A_clean_line_B_project_builds()
    {
        var (exitCode, output) = MsBuildFixture.Run("Fixture.LineB");

        Assert.True(exitCode == 0, $"build failed with exit {exitCode}:\n{output}");
    }

    /// <summary>Proves the peeked list and the derived line agree for a real Line A member, not just a fixture name.</summary>
    [Fact]
    public void A_clean_line_A_project_builds()
    {
        var (exitCode, output) = MsBuildFixture.Run(VersionLines.LineAMembers(RepoRoot)[0]);

        Assert.True(exitCode == 0, $"build failed with exit {exitCode}:\n{output}");
    }

    /// <summary>
    /// The <c>GenerateNuspec</c> hook does not reject a clean project, so packing a project that agrees with
    /// <c>VersionLines.props</c> is unaffected. <see cref="A_pack_only_override_is_caught"/> is what proves the
    /// hook is wired at all.
    /// </summary>
    [Fact]
    public void A_clean_line_B_project_packs()
    {
        var (exitCode, output) = MsBuildFixture.Run("Fixture.LineB", target: "GenerateNuspec");

        Assert.True(exitCode == 0, $"pack failed with exit {exitCode}:\n{output}");
    }

    /// <summary>
    /// Same <c>/p:ElsaVersionLine=A</c> override as the matching <see cref="MismatchCases"/> case, but through
    /// the <c>GenerateNuspec</c> hook instead of <c>BeforeBuild</c>. Mirrors packages.yml's separate <c>dotnet pack --no-build</c>
    /// invocation, which never runs <c>BeforeBuild</c>, so a pack-only <c>/p:ElsaVersionLine</c> override
    /// would otherwise sail through undetected. The fixture's own <c>GenerateNuspec</c> target stands in
    /// for NuGet's real one; this proves the wiring, not the SDK's target of the same name.
    /// </summary>
    [Fact]
    public void A_pack_only_override_is_caught()
    {
        var (exitCode, output) = MsBuildFixture.Run("Fixture.LineB", globalProperties: ["ElsaVersionLine=A"], target: "GenerateNuspec");

        Assert.True(exitCode != 0, $"expected a build failure but it succeeded:\n{output}");
        Assert.Contains("ELSAVL001", output);
    }

    [Theory]
    [MemberData(nameof(MismatchCases))]
    public void A_mismatch_fails_with_the_expected_code(
        string projectName, string body, string[] globalProperties, string expectedCode, string? expectedValue)
    {
        var (exitCode, output) = MsBuildFixture.Run(projectName, body, globalProperties);

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

        // A project file sets a line's major.minor itself, which the calculator never sees -> ELSAVL004.
        yield return new object?[]
        {
            "Fixture.LineB",
            "<PropertyGroup><ElsaVersion>4.9</ElsaVersion></PropertyGroup>",
            Array.Empty<string>(),
            "ELSAVL004",
            "'4.9'"
        };

        // /p:ElsaContractsVersion wins over VersionLines.props, for a real Line A member -> ELSAVL004.
        yield return new object?[]
        {
            lineAMember,
            "",
            new[] { "ElsaContractsVersion=5.0" },
            "ELSAVL004",
            "'5.0'"
        };
    }

    /// <summary>
    /// The <c>_GetProjectVersion</c> hook: a pack asks each project it references for its version through that target,
    /// which never runs <c>BeforeBuild</c> or <c>GenerateNuspec</c> in the referenced project, so an override there must
    /// be caught too or the referencing package's range would start at a version nothing computed.
    /// </summary>
    [Fact]
    public void An_override_seen_only_by_a_referencing_pack_is_caught()
    {
        var (exitCode, output) = MsBuildFixture.Run("Fixture.LineB", globalProperties: ["ElsaVersion=4.9"], target: "_GetProjectVersion");

        Assert.True(exitCode != 0, $"expected a build failure but it succeeded:\n{output}");
        Assert.Contains("ELSAVL004", output);
    }

    /// <summary>
    /// With a namespace-qualified <c>VersionLines.props</c>, MSBuild still imports the file just fine, but
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
            // The real list, verbatim, with only the root <Project> element namespaced.
            var root = MsBuildFixture.CopyRootBuildFiles(directory.FullName, versionLines =>
                versionLines.Replace("<Project>", "<Project xmlns=\"http://schemas.microsoft.com/developer/msbuild/2003\">"));

            var (exitCode, output) = MsBuildFixture.Run("Fixture.LineB", repositoryRoot: root);

            Assert.True(exitCode != 0, $"expected a build failure but it succeeded:\n{output}");
            Assert.Contains("ELSAVL003", output);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }
}
