using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Options;
using Elsa.Activities.Design.Core.Services;
using Elsa.Workbench.CorePreview;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.KernelHost;
using Kernel.Native;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Modularity.Tests;

/// <summary>The Workbench Core Preview's composition of the kernel shell: the feature it writes, and the toolbox it narrows.</summary>
public sealed class WorkbenchCorePreviewKernelShellTests
{
    private sealed record Activity(string ActivityTypeKey) : IActivityDefinition
    {
        public string Id => ActivityTypeKey;
        public string Category => "Test";
        public string? DisplayName => null;
        public string? Description => null;
    }

    /// <summary>A shell's toolbox as configuration leaves it: one set of its own, excluded; and the host's toolbox rule.</summary>
    private static IReadOnlyCollection<string> Addable(bool servesOnKernel)
    {
        var services = new ServiceCollection();
        services.Configure<ActivityAvailabilityOptions>(options =>
        {
            options.Sets["mine"] = ["Custom.Hidden"];
            options.Exclude.Sets = ["mine"];
        });
        if (servesOnKernel) services.AddSingleton<IWorkflowTriggerBindingExtractor, KernelTriggerBindingExtractor>();
        KernelToolbox.Register(services);
        using var shell = services.BuildServiceProvider();
        var evaluator = new DefaultActivityAvailabilityEvaluator(shell.GetRequiredService<IOptions<ActivityAvailabilityOptions>>().Value);
        var catalog = NativeActivityCatalog.Unsupported.Append("Custom.Hidden").Append("Custom.Shown").Select(key => new Activity(key));
        return [.. evaluator.FilterAddable(catalog).Select(activity => activity.ActivityTypeKey)];
    }

    [Fact]
    public void The_kernel_shell_toolbox_hides_the_eight_beside_its_own_exclusions()
    {
        Assert.Equal(8, NativeActivityCatalog.Unsupported.Count);
        Assert.Equal(["Custom.Shown"], Addable(servesOnKernel: true));
    }

    [Fact]
    public void Another_shell_toolbox_keeps_them()
    {
        Assert.Equal([.. NativeActivityCatalog.Unsupported, "Custom.Shown"], Addable(servesOnKernel: false));
    }

    [Fact]
    public void Publications_compose_the_feature_into_the_default_shell_as_shells_json_writes_it()
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection([new("Elsa:CorePreview:Enabled", "true"), new(WorkbenchCorePreview.PublicationsKey, "true")]);

        var shell = WorkbenchCorePreview.ComposeKernelShell(configuration);

        Assert.Equal("default", shell);
        var feature = Assert.Single(configuration.GetSection("CShells:Shells:default:Features").GetChildren());
        Assert.Equal((WorkflowsRuntimeKernelFeature.FeatureName, null), (feature.Key, feature.Value));
        Assert.Empty(feature.GetChildren());
    }

    [Fact]
    public void Without_publications_nothing_is_composed()
    {
        var configuration = new ConfigurationManager();
        configuration.AddInMemoryCollection([new("Elsa:CorePreview:Enabled", "true")]);

        Assert.Null(WorkbenchCorePreview.ComposeKernelShell(configuration));
        Assert.False(configuration.GetSection("CShells:Shells").Exists());
    }

    [Theory]
    [InlineData(null, "/workflows/http")]
    [InlineData("", "/workflows/http")]
    [InlineData("/kernel/", "/kernel")]
    [InlineData("hooks/in", "/hooks/in")]
    public void The_http_edge_base_path_defaults_to_the_address_studio_shows(string? configured, string expected) =>
        Assert.Equal(expected, KernelHttpEdgeHost.NormalizeBasePath(configured));

    [Fact]
    public void The_http_edge_base_path_is_never_the_root() =>
        Assert.Throws<InvalidOperationException>(() => KernelHttpEdgeHost.NormalizeBasePath("/"));

    [Theory]
    [InlineData(null, null, "default", null)]
    [InlineData("acme", null, "acme", "acme")]
    [InlineData(null, "acme", "acme", "acme")]
    [InlineData("acme", "acme", "acme", "acme")]
    public void The_core_preview_binds_one_tenant_for_its_edge_and_reads(string? tenant, string? reads, string edge, string? expectedReads) =>
        Assert.Equal((edge, expectedReads), WorkbenchCorePreview.Tenants(tenant, reads));

    [Fact]
    public void The_core_preview_refuses_two_different_tenants()
    {
        var refused = Assert.Throws<InvalidOperationException>(() => WorkbenchCorePreview.Tenants("acme", "globex"));
        Assert.Contains("one tenant per process", refused.Message, StringComparison.Ordinal);
    }
}
