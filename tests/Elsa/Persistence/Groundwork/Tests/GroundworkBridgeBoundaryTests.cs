using System.Xml.Linq;
using Xunit;

namespace Elsa.Persistence.Groundwork.Tests;

public sealed class GroundworkBridgeBoundaryTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

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
        var references = ProjectReferences(project).ToList();

        Assert.DoesNotContain(references, reference => reference.Contains("Groundwork.Sqlite", StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> ProjectReferences(string project)
    {
        var document = XDocument.Load(project);
        return document.Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .OfType<string>();
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Elsa.Server.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate repository root.");
    }
}
