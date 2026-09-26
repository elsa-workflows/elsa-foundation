using System.Xml.Linq;
using Xunit;

namespace Elsa.Architecture.Tests;

/// <summary>
/// No <c>.csproj</c> under <c>src/</c> may declare a literal <c>&lt;Version&gt;</c> (spec 150 FR-005).
/// CI's global <c>/p:Version</c> override makes a project-local <c>&lt;Version&gt;</c> inert today, so it
/// reads as though it sets the shipped version while never taking effect there; it would start shipping
/// the moment that override is removed. #2076 deleted the eighteen that had accumulated; this guard
/// keeps the count at zero.
/// </summary>
public sealed class NoLiteralProjectVersionGuardTests
{
    [Fact]
    public void No_project_under_src_declares_a_literal_version()
    {
        var scanned = 0;
        var violations = new List<string>();

        foreach (var project in Directory.EnumerateFiles(FullPath("src"), "*.csproj", SearchOption.AllDirectories)
                     .Where(path => !IsBuildOutput(path)))
        {
            scanned++;
            if (DeclaresLiteralVersion(project))
                violations.Add(RelativePath(project));
        }

        Assert.True(scanned > 0, "The literal-version guard scanned no projects under src/; its root is wrong.");
        Assert.True(
            violations.Count == 0,
            "CI's global /p:Version override makes these inert today and shipping once the override goes. " +
            $"{violations.Count} project(s) under src/ declare a literal <Version>:" +
            Environment.NewLine + string.Join(Environment.NewLine, violations.Order(StringComparer.Ordinal)));
    }

    /// <summary>
    /// Mutation proof. A synthetic project carrying a literal <c>&lt;Version&gt;</c> must be flagged by the
    /// same predicate the guard above uses; a clean project must not.
    /// </summary>
    [Fact]
    public void Guard_detects_a_synthetic_literal_version()
    {
        var directory = Directory.CreateTempSubdirectory(nameof(NoLiteralProjectVersionGuardTests));
        try
        {
            var violatingProject = Path.Combine(directory.FullName, "Violating.csproj");
            File.WriteAllText(violatingProject,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <Version>0.0.1-local</Version>
                  </PropertyGroup>
                </Project>
                """);

            var cleanProject = Path.Combine(directory.FullName, "Clean.csproj");
            File.WriteAllText(cleanProject,
                """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup>
                    <TargetFramework>net10.0</TargetFramework>
                  </PropertyGroup>
                </Project>
                """);

            Assert.True(DeclaresLiteralVersion(violatingProject));
            Assert.False(DeclaresLiteralVersion(cleanProject));
        }
        finally
        {
            directory.Delete(recursive: true);
        }
    }

    private static bool DeclaresLiteralVersion(string projectPath) =>
        XDocument.Load(projectPath).Descendants("Version").Any();

    private static bool IsBuildOutput(string path)
    {
        var normalized = path.Replace(Path.DirectorySeparatorChar, '/');
        return normalized.Contains("/bin/") || normalized.Contains("/obj/");
    }

    private static string FullPath(string relativePath) => Path.Combine(RepoRoot, relativePath);

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
