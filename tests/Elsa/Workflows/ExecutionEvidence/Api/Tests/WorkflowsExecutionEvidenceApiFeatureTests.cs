using System.Reflection;
using System.Xml.Linq;
using Elsa.Testing.Reflection;
using Elsa.Workflows.ExecutionEvidence;
using Elsa.Workflows.ExecutionEvidence.Api;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.ExecutionEvidence.Api.Tests;

public sealed class WorkflowsExecutionEvidenceApiFeatureTests
{
    [Fact]
    public void Current_skeleton_registration_builds_and_resolves_its_owned_service_set()
    {
        var services = new ServiceCollection();
        var feature = new WorkflowsExecutionEvidenceApiFeature();

        feature.ConfigureServices(services);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        // T065/T071 expand this zero-service expectation and resolve each owned contract as behavior is added.
        Assert.Empty(services);
        Assert.NotNull(provider.GetRequiredService<IServiceProvider>());
    }

    [Fact]
    public void Api_feature_inherits_and_overrides_the_provider_neutral_base_registration()
    {
        var configureServices = typeof(WorkflowsExecutionEvidenceApiFeature).GetMethod(
            nameof(WorkflowsExecutionEvidenceFeature.ConfigureServices),
            BindingFlags.Instance | BindingFlags.Public);

        Assert.Equal(typeof(WorkflowsExecutionEvidenceFeature), typeof(WorkflowsExecutionEvidenceApiFeature).BaseType);
        Assert.NotNull(configureServices);
        Assert.Equal(typeof(WorkflowsExecutionEvidenceFeature), configureServices!.GetBaseDefinition().DeclaringType);
    }

    [Fact]
    public void Api_project_composes_core_and_base_without_inmemory()
    {
        var project = XDocument.Load(Path.Combine(RepoRoot, "src", "Elsa", "Workflows", "ExecutionEvidence", "Api", "Elsa.Workflows.ExecutionEvidence.Api.csproj"));
        var references = project.Descendants("ProjectReference")
            .Select(reference => Path.GetFileNameWithoutExtension(reference.Attribute("Include")!.Value))
            .ToArray();

        Assert.Contains("Elsa.Workflows.ExecutionEvidence.Core", references);
        Assert.Contains("Elsa.Workflows.ExecutionEvidence", references);
        Assert.DoesNotContain(references, reference => reference.Contains("InMemory", StringComparison.Ordinal));
    }

    [Fact]
    public void Api_feature_reuses_the_base_registration_without_a_peer_service_locator()
    {
        var overrideMethod = typeof(WorkflowsExecutionEvidenceApiFeature).GetMethod(
            nameof(WorkflowsExecutionEvidenceApiFeature.ConfigureServices),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)!;
        var baseMethod = typeof(WorkflowsExecutionEvidenceFeature).GetMethod(
            nameof(WorkflowsExecutionEvidenceFeature.ConfigureServices),
            BindingFlags.Instance | BindingFlags.Public)!;
        var calledMethods = MethodCallInspector.GetCalledMethods(overrideMethod);

        Assert.Contains(calledMethods, method => method.Module == baseMethod.Module && method.MetadataToken == baseMethod.MetadataToken);
        Assert.DoesNotContain(calledMethods, MethodCallInspector.IsServiceLocatorOrProviderBuildCall);
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
