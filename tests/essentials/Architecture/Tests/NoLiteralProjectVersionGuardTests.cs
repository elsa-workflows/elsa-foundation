using System.Xml.Linq;
using Xunit;
using static Elsa.Architecture.Tests.RepoPaths;

namespace Elsa.Architecture.Tests;

/// <summary>
/// No <c>.csproj</c> under <c>src/</c> may hand-edit a property that determines the package version.
/// ADR 0067 says no <c>&lt;Version&gt;</c> element is hand-edited. In the .NET SDK, <c>Version</c>
/// defaults to <c>VersionPrefix[-VersionSuffix]</c>, and the <c>PackageVersion</c> property defaults
/// to <c>Version</c>; a literal in any of those three is a hand-edited package version by another
/// name, which is why they're covered too. A <c>&lt;PackageVersion Include="…"&gt;</c> item is central
/// package management (it belongs in <c>Directory.Packages.props</c>, not a csproj) and is never
/// flagged.
/// <para>
/// This guard scans project files under <c>src/</c>. Spec 150 FR-010 names shared MSBuild properties
/// as the one place for major and minor — <c>ElsaVersion</c> and <c>ElsaContractsVersion</c> in
/// <c>VersionLines.props</c> — so shared build files are deliberately not scanned here, and a project that
/// set either of those would be hand-editing its package version's major and minor, so they are forbidden
/// here too. <c>PackageVersioning.props</c> derives each package's version from them, or from the version
/// calculator's output (#2080). The build-time checks, <see cref="VersionLineBuildCheckTests"/> and
/// <see cref="PackageVersionBuildCheckTests"/>, catch what a file scan cannot: a <c>/p:</c> override, and a
/// setter in a build file outside this scan.
/// </para>
/// <para>
/// These literals never reached a published package: <c>packages.yml</c> passed a global
/// <c>/p:Version</c> when it packed, which overrode any project-local value at that step. Every other
/// build used the literal — local builds, CI's build-and-test job, Docker images — so it set the
/// assembly version of every artifact those produce. #2076 removed the eighteen <c>&lt;Version&gt;</c>
/// elements that had accumulated; like every other project, those projects now take the package version
/// <c>PackageVersioning.props</c> derives, and the SDK's default assembly version outside a computed build.
/// This guard keeps the count at zero. FR-005's other clause — no global <c>/p:Version</c> from the packaging
/// workflow — was met by #2082 and is guarded by <c>PackagesWorkflowTests</c> in the publisher's tests.
/// </para>
/// </summary>
public sealed class NoLiteralProjectVersionGuardTests
{
    private static readonly string[] ForbiddenVersionProperties =
    [
        "Version",
        "VersionPrefix",
        "VersionSuffix",
        "PackageVersion",
        "ElsaVersion",
        "ElsaContractsVersion",
    ];

    [Fact]
    public void No_project_under_src_declares_a_literal_version_property()
    {
        var scanned = 0;
        var violations = new List<string>();

        foreach (var project in ModuleRoots.Resolve(RepoRoot, ModuleRoots.Production)
                     .SelectMany(root => Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories))
                     .Where(path => !IsBuildOutput(path)))
        {
            scanned++;
            var declared = DeclaredVersionProperties(project).ToArray();
            if (declared.Length > 0)
                violations.Add($"{RelativePath(project)}: {string.Join(", ", declared.Order(StringComparer.Ordinal))}");
        }

        Assert.True(scanned > 0, "The literal-version guard scanned no projects under src/; its root is wrong.");
        Assert.True(
            violations.Count == 0,
            $"{violations.Count} project(s) under src/ hand-edit a version-defining property:" +
            Environment.NewLine + string.Join(Environment.NewLine, violations.Order(StringComparer.Ordinal)));
    }

    /// <summary>
    /// Mutation proof. A synthetic project carrying each forbidden version property must be flagged by
    /// the same predicate the guard above uses; a clean project, and a <c>PackageVersion</c> item
    /// (central package management), must not.
    /// </summary>
    [Theory]
    [InlineData("Version")]
    [InlineData("VersionPrefix")]
    [InlineData("VersionSuffix")]
    [InlineData("PackageVersion")]
    [InlineData("ElsaVersion")]
    [InlineData("ElsaContractsVersion")]
    public void Guard_detects_a_synthetic_literal_version_property(string property)
    {
        var declared = DeclaredVersionPropertiesOfTempProject(
            $"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <{property}>0.0.1-local</{property}>
              </PropertyGroup>
            </Project>
            """);

        Assert.Contains(property, declared);
    }

    [Fact]
    public void Guard_does_not_flag_a_clean_project()
    {
        var declared = DeclaredVersionPropertiesOfTempProject(
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net10.0</TargetFramework>
              </PropertyGroup>
            </Project>
            """);

        Assert.Empty(declared);
    }

    /// <summary>
    /// A <c>&lt;PackageVersion Include="…"&gt;</c> item is central package management, not a
    /// hand-edited version property, and must never be flagged.
    /// </summary>
    [Fact]
    public void Guard_does_not_flag_a_central_package_management_item()
    {
        var declared = DeclaredVersionPropertiesOfTempProject(
            """
            <Project Sdk="Microsoft.NET.Sdk">
              <ItemGroup>
                <PackageVersion Include="Some.Package" Version="1.2.3" />
              </ItemGroup>
            </Project>
            """);

        Assert.Empty(declared);
    }

    /// <summary>
    /// Writes <paramref name="projectXml"/> to a temporary project file, evaluates the guard's
    /// detection against it, and cleans up the temp directory before returning.
    /// </summary>
    private static string[] DeclaredVersionPropertiesOfTempProject(string projectXml)
    {
        var directory = Directory.CreateTempSubdirectory(nameof(NoLiteralProjectVersionGuardTests));
        try
        {
            var projectPath = Path.Join(directory.FullName, "Project.csproj");
            File.WriteAllText(projectPath, projectXml);
            return DeclaredVersionProperties(projectPath).ToArray();
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static IEnumerable<string> DeclaredVersionProperties(string projectPath) =>
        XDocument.Load(projectPath).Descendants()
            .Where(element => element.Parent?.Name.LocalName == "PropertyGroup")
            .Select(element => element.Name.LocalName)
            .Where(ForbiddenVersionProperties.Contains)
            .Distinct();

    private static string RelativePath(string path) =>
        Path.GetRelativePath(RepoRoot, path).Replace(Path.DirectorySeparatorChar, '/');
}
