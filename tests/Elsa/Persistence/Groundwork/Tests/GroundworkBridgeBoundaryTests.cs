using Groundwork.TestInfrastructure;
using System.Xml.Linq;
using Xunit;

namespace Elsa.Persistence.Groundwork.Tests;

public sealed class GroundworkBridgeBoundaryTests
{
    private static readonly string RepositoryRoot = RepositoryRootLocator.FindRepositoryRoot();
    private static readonly string[] AllowedBridgeGroundworkReferences =
    [
        "src/Groundwork/Core/Groundwork.Core.csproj",
        "src/Groundwork/Documents/Groundwork.Documents.csproj",
        "src/Groundwork/Relational/Groundwork.Relational.csproj"
    ];

    [Fact]
    public void GroundworkProjectsDoNotReferenceElsaProjects()
    {
        var violations = Directory
            .EnumerateFiles(Path.Combine(RepositoryRoot, "src", "Groundwork"), "*.csproj", SearchOption.AllDirectories)
            .SelectMany(project => ProjectReferences(project)
                .Where(reference => reference.Contains("Elsa", StringComparison.OrdinalIgnoreCase))
                .Select(reference => $"{Path.GetRelativePath(RepositoryRoot, project)} -> {reference}"))
            .ToList();

        Assert.True(violations.Count == 0, string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void ElsaBridgeDoesNotReferenceProviderSpecificGroundworkPackages()
    {
        var project = Path.Combine(RepositoryRoot, "src", "Elsa", "Persistence", "Groundwork", "Elsa.Persistence.Groundwork.csproj");
        var projectDirectory = Path.GetDirectoryName(project)!;
        var allowedReferences = AllowedBridgeGroundworkReferences.ToHashSet(StringComparer.Ordinal);
        var providerReferences = ProjectReferences(project)
            .Select(reference => NormalizeRelativeProjectPath(Path.Combine(projectDirectory, NormalizeMsBuildPath(reference))))
            .Where(reference => reference.StartsWith("src/Groundwork/", StringComparison.Ordinal) && !allowedReferences.Contains(reference))
            .ToList();

        Assert.True(providerReferences.Count == 0, string.Join(Environment.NewLine, providerReferences));
    }

    private static IEnumerable<string> ProjectReferences(string project)
    {
        var document = XDocument.Load(project);
        return document.Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .OfType<string>();
    }

    private static string NormalizeRelativeProjectPath(string path) =>
        Path.GetRelativePath(RepositoryRoot, Path.GetFullPath(path)).Replace('\\', '/');

    private static string NormalizeMsBuildPath(string path) => path.Replace('\\', Path.DirectorySeparatorChar);
}
