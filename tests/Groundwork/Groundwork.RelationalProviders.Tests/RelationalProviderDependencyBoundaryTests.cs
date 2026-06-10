using System.Xml.Linq;
using Groundwork.TestInfrastructure;
using Xunit;

namespace Groundwork.RelationalProviders.Tests;

public sealed class RelationalProviderDependencyBoundaryTests
{
    private static readonly string RepositoryRoot = RepositoryRootLocator.FindRepositoryRoot();

    [Theory]
    [InlineData("src/Groundwork/SqlServer/Groundwork.SqlServer.csproj")]
    [InlineData("src/Groundwork/PostgreSql/Groundwork.PostgreSql.csproj")]
    public void ProviderProjectsDoNotReferenceElsaProjects(string projectPath)
    {
        var project = Path.Combine(RepositoryRoot, projectPath);
        var references = XDocument.Load(project)
            .Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .OfType<string>()
            .ToList();

        Assert.All(references, reference => Assert.DoesNotContain("Elsa", reference, StringComparison.OrdinalIgnoreCase));
    }

}
