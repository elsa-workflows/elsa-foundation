using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Persistence.Groundwork.Sqlite.Tests;

public sealed class SqliteGroundworkRuntimePersistenceShellFeatureTests
{
    [Fact]
    public void SchemaAdmissionSettings_ArePublicManifestSettings_WithSafeDefaults()
    {
        var feature = new SqliteGroundworkRuntimePersistenceShellFeature();

        Assert.True(feature.AutoApplySchemaOnStartup);
        Assert.False(feature.SkipSchemaInspectionWhenPlanUnchanged);
        AssertManifestSetting(feature, nameof(feature.AutoApplySchemaOnStartup));
        AssertManifestSetting(feature, nameof(feature.SkipSchemaInspectionWhenPlanUnchanged));
    }

    [Fact]
    public void ExecutableCacheSettings_ArePublicManifestSettings_WithDurableDefaults()
    {
        var feature = new SqliteGroundworkRuntimePersistenceShellFeature();

        Assert.True(feature.CacheWorkflowExecutables);
        Assert.Equal(WorkflowExecutableCacheOptions.DefaultCapacity, feature.WorkflowExecutableCacheCapacity);
        AssertManifestSetting(feature, nameof(feature.CacheWorkflowExecutables));
        AssertManifestSetting(feature, nameof(feature.WorkflowExecutableCacheCapacity));
    }

    [Fact]
    public void AccessBoundStoreCacheSettings_ArePublicManifestSettings_WithBoundedDefaults()
    {
        var feature = new SqliteGroundworkRuntimePersistenceShellFeature();

        Assert.True(feature.ReuseAccessBoundStores);
        Assert.Equal(
            SqliteGroundworkStoreCacheOptions.DefaultCapacity,
            feature.AccessBoundStoreCacheCapacity);
        AssertManifestSetting(feature, nameof(feature.ReuseAccessBoundStores));
        AssertManifestSetting(feature, nameof(feature.AccessBoundStoreCacheCapacity));
    }

    [Fact]
    public void DisabledExecutableCacheSetting_IsThreadedToRuntimeRegistration()
    {
        var feature = new SqliteGroundworkRuntimePersistenceShellFeature
        {
            ConnectionString = "Data Source=:memory:",
            CacheWorkflowExecutables = false,
            WorkflowExecutableCacheCapacity = 17
        };
        var services = new ServiceCollection();
        feature.ConfigureServices(services);

        var options = Assert.IsType<WorkflowExecutableCacheOptions>(
            Assert.Single(
                services,
                descriptor => descriptor.ServiceType == typeof(WorkflowExecutableCacheOptions))
                .ImplementationInstance);
        Assert.False(options.Enabled);
        Assert.Equal(17, options.Capacity);
    }

    private static void AssertManifestSetting(object feature, string propertyName)
    {
        var property = feature.GetType().GetProperty(propertyName);
        Assert.NotNull(property);
        Assert.Contains(
            property!.CustomAttributes,
            attribute => attribute.AttributeType.Name == "ManifestSettingAttribute");
    }
}
