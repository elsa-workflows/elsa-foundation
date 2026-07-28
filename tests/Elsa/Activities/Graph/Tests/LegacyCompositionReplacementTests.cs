using Elsa.Workflows.Runtime.Api.Requests;
using Xunit;

namespace Elsa.Activities.Graph.Tests;

public sealed class LegacyCompositionReplacementTests
{
    [Fact]
    public void Explicit_separate_workflow_execution_request_remains_available()
    {
        var request = new ExecuteWorkflow("artifact-1");

        Assert.Equal("artifact-1", request.ArtifactId);
        Assert.Null(request.Inputs);
    }

    [Fact]
    public void Elsa4_workflow_as_activity_projects_are_removed()
    {
        var repoRoot = FindRepoRoot();

        Assert.False(File.Exists(Path.Combine(repoRoot, "src", "Elsa", "Activities", "Composition", "Design", "Elsa.Activities.Composition.Design.csproj")));
        Assert.False(File.Exists(Path.Combine(repoRoot, "src", "Elsa", "Activities", "Composition", "Runtime", "Elsa.Activities.Composition.Runtime.csproj")));
        Assert.False(File.Exists(Path.Combine(repoRoot, "src", "Elsa", "Activities", "Composition", "Runtime", "Activities", "WorkflowDefinitionActivity.cs")));
        Assert.False(File.Exists(Path.Combine(repoRoot, "src", "Elsa", "Workflows", "Design", "Core", "Models", "WorkflowActivityOptions.cs")));
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Elsa.Server.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }
}
