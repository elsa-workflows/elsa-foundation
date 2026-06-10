using System.Xml.Linq;
using Xunit;

namespace Groundwork.MongoDb.Tests;

public sealed class MongoDbDependencyBoundaryTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

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

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Elsa.Server.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate repository root.");
    }
}
