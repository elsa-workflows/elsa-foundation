using Xunit;

namespace Elsa.Activities.Design.Persistence.Groundwork.Tests;

public sealed class ActivitiesDesignV2BoundaryTests
{
    [Fact]
    public void Activity_design_groundwork_project_has_no_legacy_groundwork_boundary()
    {
        var repoRoot = FindRepositoryRoot();
        var projectPath = Path.Combine(
            repoRoot,
            "src",
            "Elsa",
            "Activities",
            "Design",
            "Persistence",
            "Groundwork",
            "Elsa.Activities.Design.Persistence.Groundwork.csproj");
        var project = File.ReadAllText(projectPath);

        Assert.DoesNotContain("Groundwork.Core", project, StringComparison.Ordinal);
        Assert.DoesNotContain("Groundwork.Documents", project, StringComparison.Ordinal);
        Assert.DoesNotContain("Elsa.Persistence.Groundwork.Composition", project, StringComparison.Ordinal);
        Assert.DoesNotContain("Elsa.Persistence.Groundwork.Querying", project, StringComparison.Ordinal);
        Assert.DoesNotContain("Elsa.Persistence.Groundwork.csproj", project, StringComparison.Ordinal);
        Assert.Contains("Elsa.Persistence.Groundwork.V2.csproj", project, StringComparison.Ordinal);
        Assert.Contains("Groundwork.Kernel", project, StringComparison.Ordinal);
        Assert.Contains("Groundwork.Query.Model", project, StringComparison.Ordinal);
        Assert.Contains("Groundwork.Store", project, StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Elsa.Server.slnx")))
                return directory.FullName;
        }

        throw new DirectoryNotFoundException("Could not locate the Elsa repository root.");
    }
}
