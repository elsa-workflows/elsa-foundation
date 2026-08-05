using System.Xml.Linq;
using CShells.Features;
using Elsa.Workflows.ExecutionEvidence;
using Elsa.Workflows.ExecutionEvidence.InMemory;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.ExecutionEvidence.InMemory.Tests;

public sealed class WorkflowsExecutionEvidenceInMemoryFeatureTests
{
    [Fact]
    public void Current_skeleton_registration_builds_and_resolves_its_owned_service_set()
    {
        var services = new ServiceCollection();
        var feature = new WorkflowsExecutionEvidenceInMemoryFeature();

        feature.ConfigureServices(services);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        // T065/T071 expand this zero-service expectation and resolve each owned contract as behavior is added.
        Assert.Empty(services);
        Assert.NotNull(provider.GetRequiredService<IServiceProvider>());
    }

    [Fact]
    public void In_memory_feature_is_an_explicit_base_specialization()
    {
        Assert.Equal(typeof(WorkflowsExecutionEvidenceFeature), typeof(WorkflowsExecutionEvidenceInMemoryFeature).BaseType);

        var feature = Assert.Single(
            typeof(WorkflowsExecutionEvidenceInMemoryFeature).GetCustomAttributes(typeof(ShellFeatureAttribute), inherit: false)
                .Cast<ShellFeatureAttribute>());

        Assert.Equal("WorkflowsExecutionEvidenceInMemory", feature.Name);
        Assert.Contains("WorkflowsExecutionEvidence", feature.DependsOn.Select(dependency => dependency?.ToString()));
    }

    [Fact]
    public void In_memory_leaf_depends_directly_on_core_and_base()
    {
        var project = XDocument.Load(Path.Combine(RepoRoot, "src", "Elsa", "Workflows", "ExecutionEvidence", "InMemory", "Elsa.Workflows.ExecutionEvidence.InMemory.csproj"));
        var references = project.Descendants("ProjectReference")
            .Select(reference => Path.GetFileNameWithoutExtension(reference.Attribute("Include")!.Value))
            .Order()
            .ToArray();

        Assert.Equal(
            [
                "Elsa.Workflows.ExecutionEvidence",
                "Elsa.Workflows.ExecutionEvidence.Core"
            ],
            references);
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
