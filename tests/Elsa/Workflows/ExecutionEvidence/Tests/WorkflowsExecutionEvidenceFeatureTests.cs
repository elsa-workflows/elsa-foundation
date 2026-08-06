using System.Reflection;
using System.Xml.Linq;
using CShells.Features;
using Elsa.Workflows.ExecutionEvidence;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.ExecutionEvidence.Tests;

public sealed class WorkflowsExecutionEvidenceFeatureTests
{
    [Fact]
    public void Current_skeleton_registration_builds_and_resolves_its_owned_service_set()
    {
        var services = new ServiceCollection();
        var feature = new WorkflowsExecutionEvidenceFeature();

        feature.ConfigureServices(services);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        // T065/T071 expand this zero-service expectation and resolve each owned contract as behavior is added.
        Assert.Empty(services);
        Assert.NotNull(provider.GetRequiredService<IServiceProvider>());
    }

    [Fact]
    public void Feature_is_public_nonsealed_and_exposes_virtual_registration()
    {
        var featureType = typeof(WorkflowsExecutionEvidenceFeature);
        var configureServices = featureType.GetMethod(nameof(WorkflowsExecutionEvidenceFeature.ConfigureServices), BindingFlags.Instance | BindingFlags.Public);

        Assert.True(featureType.IsPublic);
        Assert.False(featureType.IsSealed);
        Assert.NotNull(configureServices);
        Assert.True(configureServices!.IsVirtual);
        Assert.False(configureServices.IsFinal);
    }

    [Fact]
    public void Feature_declares_the_provider_neutral_composition_identity()
    {
        var feature = Assert.Single(
            typeof(WorkflowsExecutionEvidenceFeature).GetCustomAttributes(typeof(ShellFeatureAttribute), inherit: false)
                .Cast<ShellFeatureAttribute>());

        Assert.Equal("WorkflowsExecutionEvidence", feature.Name);
    }

    [Fact]
    public void Base_project_references_only_its_intended_foundation_projects()
    {
        var project = XDocument.Load(Path.Combine(RepoRoot, "src", "Elsa", "Workflows", "ExecutionEvidence", "Elsa.Workflows.ExecutionEvidence.csproj"));
        var references = project.Descendants("ProjectReference")
            .Select(reference => Path.GetFileNameWithoutExtension(reference.Attribute("Include")!.Value))
            .Order()
            .ToArray();

        Assert.Equal(
            [
                "Elsa.Tasks.Core",
                "Elsa.Workflows.ExecutionEvidence.Core",
                "Elsa.Workflows.Runtime.Core"
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
