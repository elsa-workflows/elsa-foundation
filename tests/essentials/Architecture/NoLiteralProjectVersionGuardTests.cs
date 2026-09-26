using System.Xml.Linq;
using Xunit;

namespace Elsa.Architecture.Tests;

/// <summary>
/// No <c>.csproj</c> under <c>src/</c> may hand-edit a version-defining property (ADR 0067, "No
/// <c>&lt;Version&gt;</c> element is hand-edited anywhere"; spec 150 FR-005, FR-010: "the version is
/// defined in one place"). This covers the whole class MSBuild recognizes for that purpose:
/// <c>Version</c>, <c>VersionPrefix</c>, <c>VersionSuffix</c>, <c>PackageVersion</c>,
/// <c>AssemblyVersion</c>, <c>FileVersion</c> and <c>InformationalVersion</c>, as properties. A
/// <c>&lt;PackageVersion Include="…"&gt;</c> item is central package management (it belongs in
/// <c>Directory.Packages.props</c>, not a csproj) and is never flagged.
/// <para>
/// These literals never reached a published package: <c>packages.yml</c> passes a global
/// <c>/p:Version</c> when it packs, which overrides any project-local value at that step. Every other
/// build used the literal — local builds, CI's build-and-test job, Docker images — so it set the
/// assembly version of every artifact those produce. #2076 removed the eighteen <c>&lt;Version&gt;</c>
/// elements that had accumulated; those projects now take the SDK default like every other project,
/// which is why removing one moved <c>Elsa.Diagnostics.StructuredLogs.Core</c>'s assembly version from
/// 0.0.1.0 to 1.0.0.0. This guard keeps the count at zero. FR-005's other clause — the packaging
/// workflow's global <c>/p:Version</c> — is removed and guarded separately, in #2082.
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
        "AssemblyVersion",
        "FileVersion",
        "InformationalVersion",
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
    [InlineData("AssemblyVersion")]
    [InlineData("FileVersion")]
    [InlineData("InformationalVersion")]
    public void Guard_detects_a_synthetic_literal_version_property(string property)
    {
        var directory = Directory.CreateTempSubdirectory(nameof(NoLiteralProjectVersionGuardTests));
        try
        {
            var violatingProject = Path.Combine(directory.FullName, "Violating.csproj");
            File.WriteAllText(violatingProject,
                $"""
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <{property}>0.0.1-local</{property}>
                  </PropertyGroup>
                </Project>
                """);

            Assert.Contains(property, DeclaredVersionProperties(violatingProject));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    [Fact]
    public void Guard_does_not_flag_a_clean_project()
    {
        var directory = Directory.CreateTempSubdirectory(nameof(NoLiteralProjectVersionGuardTests));
        try
        {
            var cleanProject = Path.Combine(directory.FullName, "Clean.csproj");
            File.WriteAllText(cleanProject,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """);

            Assert.Empty(DeclaredVersionProperties(cleanProject));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    /// <summary>
    /// A <c>&lt;PackageVersion Include="…"&gt;</c> item is central package management, not a
    /// hand-edited version property, and must never be flagged.
    /// </summary>
    [Fact]
    public void Guard_does_not_flag_a_central_package_management_item()
    {
        var directory = Directory.CreateTempSubdirectory(nameof(NoLiteralProjectVersionGuardTests));
        try
        {
            var itemProject = Path.Combine(directory.FullName, "CentralPackageManagement.csproj");
            File.WriteAllText(itemProject,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <ItemGroup>
                    <PackageVersion Include="Some.Package" Version="1.2.3" />
                  </ItemGroup>
                </Project>
                """);

            Assert.Empty(DeclaredVersionProperties(itemProject));
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

    private static bool IsBuildOutput(string path)
    {
        var normalized = path.Replace(Path.DirectorySeparatorChar, '/');
        return normalized.Contains("/bin/") || normalized.Contains("/obj/");
    }

    private static string RelativePath(string path) =>
        Path.GetRelativePath(RepoRoot, path).Replace(Path.DirectorySeparatorChar, '/');

    private static string RepoRoot { get; } = FindRepoRoot();

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Elsa.Server.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate the repository root.");
    }
}
