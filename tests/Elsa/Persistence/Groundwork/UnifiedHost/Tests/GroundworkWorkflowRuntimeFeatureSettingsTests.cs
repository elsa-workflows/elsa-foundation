using Elsa.Persistence.Groundwork.Runtime;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Persistence.Groundwork.UnifiedHost.Tests;

/// <summary>Verifies that the host-owned runtime feature keeps its public cache settings.</summary>
public sealed class GroundworkWorkflowRuntimeFeatureSettingsTests
{
    [Fact]
    public void Settings_have_durable_defaults_and_manifest_metadata()
    {
        var feature = new GroundworkWorkflowRuntimeFeature();

        Assert.True(feature.CacheWorkflowExecutables);
        Assert.Equal(WorkflowExecutableCacheOptions.DefaultCapacity, feature.WorkflowExecutableCacheCapacity);
        Assert.Contains(
            typeof(GroundworkWorkflowRuntimeFeature).GetProperty(nameof(feature.CacheWorkflowExecutables))!.CustomAttributes,
            attribute => attribute.AttributeType.Name == "ManifestSettingAttribute");
        Assert.Contains(
            typeof(GroundworkWorkflowRuntimeFeature).GetProperty(nameof(feature.WorkflowExecutableCacheCapacity))!.CustomAttributes,
            attribute => attribute.AttributeType.Name == "ManifestSettingAttribute");
    }

    [Fact]
    public void Settings_are_threaded_to_the_runtime_registration()
    {
        var feature = new GroundworkWorkflowRuntimeFeature
        {
            CacheWorkflowExecutables = false,
            WorkflowExecutableCacheCapacity = 29
        };
        var services = new ServiceCollection();

        feature.ConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<WorkflowExecutableCacheOptions>();
        Assert.False(options.Enabled);
        Assert.Equal(29, options.Capacity);
    }
}
