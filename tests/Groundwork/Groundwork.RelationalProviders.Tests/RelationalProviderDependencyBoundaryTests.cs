using System.Xml.Linq;
using Xunit;

namespace Groundwork.RelationalProviders.Tests;

public sealed class RelationalProviderDependencyBoundaryTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

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

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Elsa.Server.slnx")))
            directory = directory.Parent;

        return directory?.FullName ?? throw new InvalidOperationException("Could not locate repository root.");
    }
}
