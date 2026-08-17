using CShells.Features;
using Elsa.Workflows.Runtime.Reconciliation.Contracts;
using Elsa.Workflows.Runtime.Reconciliation.Core.Contracts;
using Elsa.Workflows.Runtime.Reconciliation.Core.Options;
using Elsa.Workflows.Runtime.Reconciliation.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Xunit;

namespace Elsa.Workflows.Runtime.Reconciliation.Tests;

/// <summary>
/// Framework §2.23.1 registration coverage for <see cref="JsonWorkflowArtifactReconciliationFeature"/>: the shell
/// identity and dependency declaration, the exactly-one composition gate, and the two collaborators it contributes
/// on top of the base feature.
/// </summary>
public sealed class JsonWorkflowArtifactReconciliationFeatureTests
{
    [Fact]
    public void Declares_its_shell_identity_and_dependencies()
    {
        var attribute = Assert.Single(
            typeof(JsonWorkflowArtifactReconciliationFeature)
                .GetCustomAttributes(typeof(ShellFeatureAttribute), inherit: false)
                .Cast<ShellFeatureAttribute>());

        Assert.Equal("JsonWorkflowArtifactReconciliation", attribute.Name);

        var dependencies = attribute.DependsOn.Select(dependency => dependency?.ToString()).ToArray();
        Assert.Contains("Tasks", dependencies);
        // Not merely AddWorkflowRuntime(): the binding/schedule/indexer spine the activation coordinator projects
        // into is registered by the triggers feature, and without it an imported timer-started workflow would be
        // live and unable to ever fire.
        Assert.Contains("WorkflowsRuntimeTriggers", dependencies);
    }

    [Fact]
    public void Feature_is_public_and_not_sealed()
    {
        var type = typeof(JsonWorkflowArtifactReconciliationFeature);

        Assert.True(type.IsPublic);
        Assert.False(type.IsSealed);
        Assert.Equal(typeof(WorkflowsArtifactReconciliationFeature), type.BaseType);
    }

    [Fact]
    public void Registers_the_closure_reader_and_the_json_source()
    {
        using var provider = WorkflowsArtifactReconciliationFeatureRegistrationTests.Build(
            CreateFeature(options => options.FolderPath = "/mnt/artifacts"));

        using var scope = provider.CreateScope();
        Assert.IsType<JsonWorkflowArtifactClosureReader>(scope.ServiceProvider.GetRequiredService<IWorkflowArtifactClosureReader>());
        var source = Assert.Single(scope.ServiceProvider.GetServices<IWorkflowArtifactReconciliationSource>());
        Assert.IsType<JsonWorkflowArtifactReconciliationSource>(source);
        Assert.Equal("mounted-artifacts", source.SourceId);
        Assert.Equal("Json", source.SourceKind);
    }

    [Fact]
    public void Registers_the_reconciler_from_the_base_feature()
    {
        using var provider = WorkflowsArtifactReconciliationFeatureRegistrationTests.Build(
            CreateFeature(options => options.FolderPath = "/mnt/artifacts"));

        using var scope = provider.CreateScope();
        Assert.IsType<WorkflowArtifactReconciler>(scope.ServiceProvider.GetRequiredService<IWorkflowArtifactReconciler>());
    }

    [Fact]
    public void Registers_its_source_options()
    {
        using var provider = WorkflowsArtifactReconciliationFeatureRegistrationTests.Build(
            CreateFeature(options =>
            {
                options.FolderPath = "/mnt/artifacts";
                options.TenantId = "tenant-a";
            }));

        var options = provider.GetRequiredService<IOptions<JsonWorkflowArtifactReconciliationOptions>>().Value;

        Assert.Equal("/mnt/artifacts", options.FolderPath);
        Assert.Equal("tenant-a", options.TenantId);
    }

    [Theory]
    [InlineData("closure.json", false, null)]
    [InlineData(null, true, null)]
    [InlineData(null, false, "/mnt/artifacts")]
    public void ConfigureServices_accepts_exactly_one_configured_shape(string? filePath, bool files, string? folderPath)
    {
        var feature = CreateFeature(options =>
        {
            options.FilePath = filePath;
            options.Files = files ? [new JsonWorkflowArtifactReconciliationFileOption(1, "closure.json")] : [];
            options.FolderPath = folderPath;
        });

        var services = new ServiceCollection();
        feature.ConfigureServices(services);

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IWorkflowArtifactReconciliationSource));
    }

    [Theory]
    [InlineData("closure.json", true, null)]
    [InlineData("closure.json", false, "/mnt/artifacts")]
    [InlineData(null, true, "/mnt/artifacts")]
    [InlineData("closure.json", true, "/mnt/artifacts")]
    [InlineData(null, false, null)]
    public void ConfigureServices_rejects_anything_but_exactly_one_shape(string? filePath, bool files, string? folderPath)
    {
        var feature = CreateFeature(options =>
        {
            options.FilePath = filePath;
            options.Files = files ? [new JsonWorkflowArtifactReconciliationFileOption(1, "closure.json")] : [];
            options.FolderPath = folderPath;
        });

        var exception = Assert.Throws<InvalidOperationException>(() => feature.ConfigureServices(new ServiceCollection()));

        Assert.Contains("exactly one", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfigureServices_requires_a_source_id()
    {
        // SourceId is the activation ownership descriptor, so it is chosen deliberately rather than derived from a
        // mount path that would change the owner whenever the mount moves.
        var feature = new JsonWorkflowArtifactReconciliationFeature
        {
            Options = { FolderPath = "/mnt/artifacts" },
        };

        var exception = Assert.Throws<InvalidOperationException>(() => feature.ConfigureServices(new ServiceCollection()));

        Assert.Contains(nameof(JsonWorkflowArtifactReconciliationOptions.SourceId), exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ConfigureServices_validates_before_it_registers_anything()
    {
        var feature = CreateFeature(_ => { });
        var services = new ServiceCollection();

        Assert.Throws<InvalidOperationException>(() => feature.ConfigureServices(services));

        Assert.Empty(services);
    }

    private static JsonWorkflowArtifactReconciliationFeature CreateFeature(
        Action<JsonWorkflowArtifactReconciliationOptions> configure)
    {
        var feature = new JsonWorkflowArtifactReconciliationFeature
        {
            Options = { SourceId = "mounted-artifacts" },
        };
        configure(feature.Options);
        return feature;
    }
}
