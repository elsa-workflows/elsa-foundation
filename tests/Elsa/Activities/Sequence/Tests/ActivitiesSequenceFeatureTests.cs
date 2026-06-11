using Elsa.Activities.Sequence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Sequence.Tests;

public sealed class ActivitiesSequenceFeatureTests
{
    [Fact]
    public void ConfigureServices_DoesNotRequireDesignProjects()
    {
        var services = new ServiceCollection();

        new ActivitiesSequenceFeature().ConfigureServices(services);

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
        return Path.Combine(directory.FullName, "src", "Elsa", "Activities", "Sequence", "Elsa.Activities.Sequence.csproj");
    }
}
