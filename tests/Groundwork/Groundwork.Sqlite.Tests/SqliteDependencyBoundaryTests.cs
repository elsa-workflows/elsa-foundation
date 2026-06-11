using System.Xml.Linq;
using Groundwork.TestInfrastructure;
using Xunit;

namespace Groundwork.Sqlite.Tests;

public sealed class SqliteDependencyBoundaryTests
{
    private static readonly string RepositoryRoot = RepositoryRootLocator.FindRepositoryRoot();

    [Fact]
    public void GroundworkSqliteDoesNotReferenceElsaProjects()
    {
        var project = Path.Combine(RepositoryRoot, "src/Groundwork/Sqlite/Groundwork.Sqlite.csproj");
        var references = XDocument.Load(project)
            .Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .OfType<string>()
            .ToList();

        Assert.All(references, reference => Assert.DoesNotContain("Elsa", reference, StringComparison.OrdinalIgnoreCase));
    }

}
