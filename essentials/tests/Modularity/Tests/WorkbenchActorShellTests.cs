using Elsa.Activities.Design.Api;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Options;
using Elsa.Activities.Design.Core.Services;
using Elsa.Workbench.Composition;
using Elsa.Workflows.Runtime.KernelHosting;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.KernelHost;
using Kernel.Native;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Modularity.Tests;

/// <summary>Tests the Workbench Actor toolbox and production configuration.</summary>
public sealed class WorkbenchActorShellTests
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
        new ActivitiesDesignApiFeature().ConfigureServices(services);
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

    [Theory]
    [InlineData(null)]
    [InlineData("false")]
    public void Production_actor_host_keeps_unselected_artifact_persistence_unconfigured(string? selection)
    {
        const string features = "CShells:Shells:default:Features";
        const string artifact = $"{features}:WorkflowsRuntimeArtifactsEntityFrameworkCorePersistence";
        using var configuration = new ConfigurationManager();
        configuration[$"{features}:WorkflowsRuntimeKernelHosting:Provider"] = "PostgreSql";
        if (selection is not null)
            configuration[artifact] = selection;
        var environment = new HostingEnvironment { EnvironmentName = Environments.Production };

        WorkbenchRuntimeProfile.Configure(configuration, environment, []);

        Assert.Equal(selection, configuration[artifact]);
        Assert.Empty(configuration.GetSection(artifact).GetChildren());
    }
}
