using System.Xml.Linq;
using Xunit;
using static Elsa.Architecture.Tests.RepoPaths;

namespace Elsa.Architecture.Tests;

/// <summary>
/// Line A membership (ADR 0067) is one reviewed list: <c>&lt;ElsaVersionLineAMembers&gt;</c> in the root
/// <c>VersionLines.props</c>. The root <c>Directory.Build.props</c> imports that file and derives
/// <c>$(ElsaVersionLine)</c> from it (<c>B</c> by default, <c>A</c> when the project's name appears in the
/// list). Nothing in MSBuild stops a project file, or a nested <c>Directory.Build.props</c>/<c>.targets</c>,
/// from setting either property itself. If one did, MSBuild's notion of the project's line would silently
/// disagree with the reviewed list and with <c>docs/maps/dependency-map.json</c> (generated from the same
/// list by <c>tools/maps/Elsa.Maps.Generator</c>), and both #2078 (PublicApiAnalyzers gated on
/// <c>$(ElsaVersionLine) == 'A'</c>) and #2080 (version computation reading the same property) would act on
/// the wrong line for that project without either failing loudly.
/// </summary>
/// <remarks>
/// This guard scans every <c>.csproj</c>, <c>.props</c>, and <c>.targets</c> file under <c>src/</c> and
/// <c>tests/</c> — not just <c>.csproj</c>, because a <c>Directory.Build.props</c> or a <c>.targets</c> file
/// is exactly where a stray setter would land, and a <c>PropertyGroup</c> nested inside a <c>&lt;Target&gt;</c>
/// counts too. The root <c>Directory.Build.props</c> and <c>VersionLines.props</c> live outside both
/// <c>src/</c> and <c>tests/</c>, so they are never part of this scan; they are the two sanctioned setters,
/// checked separately below. The root <c>Directory.Build.props</c> also checks the evaluated values at
/// build time (its <c>ElsaVerifyVersionLine</c> target, proved by <see cref="VersionLineBuildCheckTests"/>),
/// which covers a <c>/p:</c> override and an evaluation-time setter outside <c>src/</c> and <c>tests/</c> that
/// this scan cannot see. Property-name matching is case-insensitive, like MSBuild's own property names, so a
/// lowercase <c>&lt;elsaversionline&gt;</c> element sets the real property too. Part of #1144.
/// </remarks>
public sealed class VersionLineOwnershipGuardTests
{
    private static readonly string[] OwnedProperties = ["ElsaVersionLine", "ElsaVersionLineAMembers"];

    private static readonly string[] ScannedExtensions = [".csproj", ".props", ".targets"];

    [Fact]
    public void No_file_under_src_or_tests_sets_a_version_line_property()
    {
        var (scanned, violations) = Scan(ModuleRoots.Resolve(RepoRoot, ModuleRoots.All));

        Assert.True(scanned.Count > 0, "The version-line ownership guard scanned no files under src/ or tests/; its root is wrong.");

        var expectedRoots = new[] { "src/essentials", "src/extensions", "src/apps", "tests" };
        var missingRoots = expectedRoots
            .Where(root => !scanned.Any(path => RelativePath(path).StartsWith($"{root}/", StringComparison.Ordinal)))
            .ToArray();
        Assert.True(missingRoots.Length == 0, $"No scanned file came from: {string.Join(", ", missingRoots)}.");

        var scannedSet = scanned.Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);

        Assert.Contains(Path.GetFullPath(Path.Join(RepoRoot, "tests", "Directory.Build.props")), scannedSet);

        var missingProjects = ProjectGraph.ElsaProjectPaths(RepoRoot)
            .Select(Path.GetFullPath)
            .Where(path => !scannedSet.Contains(path))
            .ToArray();
        Assert.True(
            missingProjects.Length == 0,
            $"{missingProjects.Length} Elsa project(s) were not covered by this scan, an independent " +
            $"enumeration: {string.Join(", ", missingProjects.Select(RelativePath).Order(StringComparer.Ordinal))}");

        Assert.True(
            violations.Count == 0,
            $"{violations.Count} file(s) under src/ or tests/ set a version-line property. The one place " +
            "to change Line A membership is VersionLines.props:" +
            Environment.NewLine + string.Join(Environment.NewLine, violations.Order(StringComparer.Ordinal)));
    }

    /// <summary>
    /// Proves the guard is looking for the live property names, not restating them. If either property were
    /// renamed in the root files without updating <see cref="OwnedProperties"/>, this fails instead of
    /// leaving the guard silently scanning for a name nothing sets any more.
    /// </summary>
    [Theory]
    [InlineData("Directory.Build.props", "ElsaVersionLine")]
    [InlineData("VersionLines.props", "ElsaVersionLineAMembers")]
    public void Guard_detects_the_sanctioned_setters(string fileName, string property) =>
        Assert.Contains(property, OwnedPropertiesSet(Path.Join(RepoRoot, fileName)));

    /// <summary>
    /// Fixture mutation proof. A <c>Directory.Build.props</c> and a <c>.targets</c> file (the latter setting
    /// the property inside a <c>&lt;Target&gt;</c>'s own <c>PropertyGroup</c>) are both flagged; a NuGet-restore
    /// generated file under <c>obj/</c> that sets the same property is excluded from the scan entirely, not
    /// merely un-flagged, because <c>obj/</c> holds generated <c>.props</c>/<c>.targets</c> that are not
    /// hand-authored and would otherwise make every restore a false positive.
    /// </summary>
    [Theory]
    [InlineData("ElsaVersionLine")]
    [InlineData("ElsaVersionLineAMembers")]
    public void Guard_ignores_build_output_and_catches_a_synthetic_setter_everywhere_else(string property)
    {
        var (scanned, violations) = ScanTempTree(new Dictionary<string, string>
        {
            ["src/Some.Module/Directory.Build.props"] = SettingProject(property),
            ["src/Some.Module/Some.Module.csproj"] = CleanProject,
            ["src/Some.Module/obj/Some.Module.csproj.nuget.g.props"] = SettingProject(property),
            ["src/Some.Module/Build.targets"] = $"""
                <Project>
                  <Target Name="X">
                    <PropertyGroup>
                      <{property}>A</{property}>
                    </PropertyGroup>
                  </Target>
                </Project>
                """,
        });

        Assert.Equal(3, scanned.Count);
        Assert.Equal(2, violations.Count);
        Assert.Contains(violations, violation => violation.EndsWith($"Directory.Build.props: {property}", StringComparison.Ordinal));
        Assert.Contains(violations, violation => violation.EndsWith($"Build.targets: {property}", StringComparison.Ordinal));
        Assert.DoesNotContain(violations, violation => violation.Contains("/obj/", StringComparison.Ordinal));
    }

    /// <summary>
    /// MSBuild property names are case-insensitive, so a lowercase setter is caught too, and the violation
    /// reports the canonical name from <see cref="OwnedProperties"/> rather than the as-written casing,
    /// keeping messages stable regardless of how a file spells the property.
    /// </summary>
    [Fact]
    public void Guard_catches_a_lowercase_setter_and_reports_the_canonical_name()
    {
        var (_, violations) = ScanTempTree(new Dictionary<string, string>
        {
            ["Some.Module.csproj"] = SettingProject("elsaversionline"),
        });

        Assert.Contains(violations, violation => violation.EndsWith("Some.Module.csproj: ElsaVersionLine", StringComparison.Ordinal));
    }

    /// <summary>
    /// Reading the property — in a <c>Condition</c> attribute or as a <c>$(...)</c> substitution in an
    /// element's text — is not a set and must never be flagged.
    /// </summary>
    [Fact]
    public void Guard_does_not_flag_reading_the_property()
    {
        var (_, violations) = ScanTempTree(new Dictionary<string, string>
        {
            ["Some.Module.csproj"] = """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup Condition="'$(ElsaVersionLine)' == 'A'">
                    <Compile Include="Foo.cs" />
                  </ItemGroup>
                  <PropertyGroup>
                    <Foo>$(ElsaVersionLineAMembers)</Foo>
                  </PropertyGroup>
                </Project>
                """,
        });

        Assert.Empty(violations);
    }

    private static string SettingProject(string property) =>
        $"""
        <Project>
          <PropertyGroup>
            <{property}>A</{property}>
          </PropertyGroup>
        </Project>
        """;

    private const string CleanProject =
        """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
          </PropertyGroup>
        </Project>
        """;

    /// <summary>
    /// Scans every <c>.csproj</c>/<c>.props</c>/<c>.targets</c> file under the given roots, excluding build
    /// output, and reports both the scanned paths (so callers can prove the sweep isn't vacuous) and the
    /// paths that set an owned property.
    /// </summary>
    private static (IReadOnlyList<string> Scanned, IReadOnlyList<string> Violations) Scan(IEnumerable<string> roots)
    {
        var scanned = new List<string>();
        var violations = new List<string>();

        foreach (var file in roots
                     .SelectMany(root => Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                     .Where(path => ScannedExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
                     .Where(path => !IsBuildOutput(path)))
        {
            scanned.Add(file);
            var owned = OwnedPropertiesSet(file).ToArray();
            if (owned.Length > 0)
                violations.Add($"{RelativePath(file)}: {string.Join(", ", owned.Order(StringComparer.Ordinal))}");
        }

        return (scanned, violations);
    }

    /// <summary>
    /// Writes <paramref name="filesByRelativePath"/> into a fresh temp directory, runs <see cref="Scan"/>
    /// over it, and cleans up before returning.
    /// </summary>
    private static (IReadOnlyList<string> Scanned, IReadOnlyList<string> Violations) ScanTempTree(
        IReadOnlyDictionary<string, string> filesByRelativePath)
    {
        var directory = Directory.CreateTempSubdirectory(nameof(VersionLineOwnershipGuardTests));
        try
        {
            foreach (var (relativePath, contents) in filesByRelativePath)
            {
                var path = Path.Join(directory.FullName, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, contents);
            }

            return Scan([directory.FullName]);
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static IEnumerable<string> OwnedPropertiesSet(string path) =>
        XDocument.Load(path).Descendants()
            .Where(element => element.Parent?.Name.LocalName == "PropertyGroup")
            .Select(element => OwnedProperties.FirstOrDefault(
                owned => string.Equals(owned, element.Name.LocalName, StringComparison.OrdinalIgnoreCase)))
            .OfType<string>()
            .Distinct();

    private static string RelativePath(string path) =>
        Path.GetRelativePath(RepoRoot, path).Replace(Path.DirectorySeparatorChar, '/');
}
