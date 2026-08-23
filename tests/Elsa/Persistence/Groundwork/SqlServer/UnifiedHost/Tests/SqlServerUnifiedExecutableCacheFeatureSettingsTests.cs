using Elsa.Persistence.Groundwork.SqlServer.Unified;
using Elsa.Persistence.Groundwork.SqlServer.Unified.DependencyInjection;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Persistence.Groundwork.SqlServer.Tests;

public sealed class SqlServerUnifiedExecutableCacheFeatureSettingsTests
{
    [Fact]
    public void Original_registration_enables_the_bounded_cache_by_default()
    {
        var services = new ServiceCollection();
        services.AddGroundworkSqlServerUnifiedPersistence("Server=localhost;Database=elsa");
        using var provider = services.BuildServiceProvider();

        Assert.True(provider.GetRequiredService<WorkflowExecutableCacheOptions>().Enabled);
    }

    [Fact]
    public void Explicit_registration_rejects_null_options_before_mutating_services()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentNullException>(() =>
            services.AddGroundworkSqlServerUnifiedPersistence(
                "Server=localhost;Database=elsa",
                (WorkflowExecutableCacheOptions)null!));
        Assert.Empty(services);
    }

    [Fact]
    public void Feature_settings_have_durable_defaults_and_thread_configuration()
    {
        var feature = NewFeature();

        Assert.True(feature.CacheWorkflowExecutables);
        Assert.Equal(WorkflowExecutableCacheOptions.DefaultCapacity, feature.WorkflowExecutableCacheCapacity);
        Assert.Contains(
            typeof(SqlServerGroundworkUnifiedPersistenceShellFeature)
                .GetProperty(nameof(feature.CacheWorkflowExecutables))!.CustomAttributes,
            attribute => attribute.AttributeType.Name == "ManifestSettingAttribute");

        feature.CacheWorkflowExecutables = false;
        feature.WorkflowExecutableCacheCapacity = 31;
        var services = new ServiceCollection();
        feature.ConfigureServices(services);
        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<WorkflowExecutableCacheOptions>();

        Assert.False(options.Enabled);
        Assert.Equal(31, options.Capacity);
    }

    private static SqlServerGroundworkUnifiedPersistenceShellFeature NewFeature() =>
        new();
}
