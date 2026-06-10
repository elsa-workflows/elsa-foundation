using System.Xml.Linq;
using Groundwork.TestInfrastructure;
using Xunit;

namespace Groundwork.MongoDb.Tests;

public sealed class MongoDbDependencyBoundaryTests
{
    private static readonly string RepositoryRoot = RepositoryRootLocator.FindRepositoryRoot();

    [Fact]
    public void GroundworkMongoDbDoesNotReferenceElsaProjects()
    {
        var project = Path.Combine(RepositoryRoot, "src/Groundwork/MongoDb/Groundwork.MongoDb.csproj");
        var references = XDocument.Load(project)
            .Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .OfType<string>()
            .ToList();

        Assert.All(references, reference => Assert.DoesNotContain("Elsa", reference, StringComparison.OrdinalIgnoreCase));
    }

}
