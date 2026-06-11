using Elsa.Activities.Flowchart;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Flowchart.Tests;

public sealed class ActivitiesFlowchartFeatureTests
{
    [Fact]
    public void ConfigureServices_DoesNotRequireDesignProjects()
    {
        var services = new ServiceCollection();

        new ActivitiesFlowchartFeature().ConfigureServices(services);

        var projectFile = File.ReadAllText(ProjectFilePath());
        Assert.DoesNotContain("Design", projectFile, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Elsa.Activities.Runtime.Core.csproj", projectFile, StringComparison.Ordinal);
        Assert.Contains("Elsa.Workflows.Runtime.Core.csproj", projectFile, StringComparison.Ordinal);
    }

    private static string ProjectFilePath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Elsa.Server.slnx")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        return Path.Combine(directory.FullName, "src", "Elsa", "Activities", "Flowchart", "Elsa.Activities.Flowchart.csproj");
    }
}
