using System.Xml.Linq;
using Xunit;
using Xunit.Abstractions;
using static Elsa.Architecture.Tests.RepoPaths;

namespace Elsa.Architecture.Tests;

/// <summary>
/// Migration ratchets for spec 095. Temporary allowlists may only shrink.
/// </summary>
public sealed class ValueFlowArchitectureTests(ITestOutputHelper output)
{
    private static readonly HashSet<(string Project, string Reference)> RuntimeDesignMigrationAllowlist = [];

    [Fact]
    public void RuntimeProjects_DoNotAddDesignReferencesBeyondMigrationAllowlist()
    {
        var references = ProductionProjects()
            .Where(project => project.Name.Contains(".Runtime", StringComparison.Ordinal))
            .SelectMany(project => ProjectReferences(project)
                .Where(reference => reference.Contains(".Design", StringComparison.Ordinal))
                .Select(reference => (Project: project.Name, Reference: reference)))
            .OrderBy(edge => edge.Project, StringComparer.Ordinal)
            .ThenBy(edge => edge.Reference, StringComparer.Ordinal)
            .ToArray();
        var violations = references.Where(edge => !RuntimeDesignMigrationAllowlist.Contains(edge)).ToArray();

        ReportAllowlisted("Runtime -> Design references pending migration", references.Where(RuntimeDesignMigrationAllowlist.Contains));
        Assert.True(
            violations.Length == 0,
            "Runtime projects must not add Design references. Unexpected edges:" + Environment.NewLine +
            string.Join(Environment.NewLine, violations.Select(FormatEdge)));
    }

    [Fact]
    public void RoslynAndGeneratedAuthoringCarriers_RemainOutsideRuntime()
    {
        var projects = ProductionProjects().ToArray();
        var roslynPackageViolations = projects
            .SelectMany(project => PackageReferences(project)
                .Where(package => package.Name.StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal))
                .Where(package => project.Name != "Elsa.Workflows.Design.CodeGeneration" || !package.PrivateAssetsAll)
                .Select(package => $"{project.Name} -> {package.Name} (PrivateAssets=all: {package.PrivateAssetsAll})"))
            .ToArray();
        var runtimeGeneratorReferences = projects
            .Where(project => project.Name.Contains(".Runtime", StringComparison.Ordinal))
            .SelectMany(project => ProjectReferences(project)
                .Where(reference => reference.Contains("CodeGeneration", StringComparison.Ordinal))
                .Select(reference => $"{project.Name} -> {reference}"))
            .ToArray();
        var violations = roslynPackageViolations.Concat(runtimeGeneratorReferences).ToArray();

        Assert.True(
            violations.Length == 0,
            "Roslyn and generated authoring carriers belong only to the Design code-generation envelope; " +
            "Runtime must consume canonical artifacts:" + Environment.NewLine + string.Join(Environment.NewLine, violations));
    }

    private void ReportAllowlisted<T>(string heading, IEnumerable<T> values)
    {
        var entries = values.Select(value => value?.ToString()).Where(value => value is not null).ToArray();
        if (entries.Length == 0)
            return;

        output.WriteLine(heading + ":");
        foreach (var entry in entries)
            output.WriteLine("  " + entry);
    }

    private static string FormatEdge((string Project, string Reference) edge) => $"{edge.Project} -> {edge.Reference}";

    private static IEnumerable<ProjectInfo> ProductionProjects() =>
        Directory.EnumerateFiles(Path.Combine(RepoRoot, "src", "essentials"), "*.csproj", SearchOption.AllDirectories)
            .Select(path => new ProjectInfo(Path.GetFileNameWithoutExtension(path), path));

    private static IEnumerable<string> ProjectReferences(ProjectInfo project)
    {
        var document = XDocument.Load(project.FullPath);
        return document.Descendants("ProjectReference")
            .Select(reference => reference.Attribute("Include")?.Value)
            .OfType<string>()
            .Select(include => Path.GetFileNameWithoutExtension(include.Replace('\\', Path.DirectorySeparatorChar)));
    }

    private static IEnumerable<PackageReferenceInfo> PackageReferences(ProjectInfo project)
    {
        var document = XDocument.Load(project.FullPath);
        return document.Descendants("PackageReference")
            .Select(reference => new PackageReferenceInfo(
                reference.Attribute("Include")?.Value ?? string.Empty,
                string.Equals(
                    reference.Attribute("PrivateAssets")?.Value ?? reference.Element("PrivateAssets")?.Value,
                    "all",
                    StringComparison.OrdinalIgnoreCase)));
    }

    private sealed record ProjectInfo(string Name, string FullPath);

    private sealed record PackageReferenceInfo(string Name, bool PrivateAssetsAll);
}
