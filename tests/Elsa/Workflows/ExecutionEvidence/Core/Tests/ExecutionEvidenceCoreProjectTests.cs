using System.Xml.Linq;
using Xunit;

namespace Elsa.Workflows.ExecutionEvidence.Core.Tests;

public sealed class ExecutionEvidenceCoreProjectTests
{
    [Fact]
    public void Core_project_is_contracts_only()
    {
        var project = XDocument.Load(Path.Combine(RepoRoot, "src", "Elsa", "Workflows", "ExecutionEvidence", "Core", "Elsa.Workflows.ExecutionEvidence.Core.csproj"));
        var projectReferences = project.Descendants("ProjectReference")
            .Select(reference => reference.Attribute("Include")?.Value)
            .Where(reference => !string.IsNullOrWhiteSpace(reference))
            .ToArray();
        var packageReferences = project.Descendants("PackageReference")
            .Select(reference => reference.Attribute("Include")?.Value)
            .Where(reference => !string.IsNullOrWhiteSpace(reference))
            .ToArray();

        Assert.Empty(projectReferences);
        Assert.DoesNotContain(packageReferences, reference =>
            reference!.Contains("Runtime", StringComparison.Ordinal) ||
            reference.Contains("InMemory", StringComparison.Ordinal) ||
            reference.Contains("AspNetCore", StringComparison.Ordinal) ||
            reference.Contains("FastEndpoints", StringComparison.Ordinal) ||
            reference.Contains("xunit", StringComparison.OrdinalIgnoreCase));
    }

    private static string RepoRoot
    {
        get
        {
            for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Elsa.Server.slnx")))
                    return directory.FullName;
            }

            throw new InvalidOperationException("Could not locate the repository root.");
        }
    }
}
