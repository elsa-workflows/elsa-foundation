using CShells;
using CShells.Features;
using Elsa.Persistence.Groundwork.Sqlite.Unified;
using Elsa.Persistence.Groundwork.Sqlite.Unified.DependencyInjection;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Persistence.Groundwork.UnifiedHost.Tests;

public sealed class SqliteUnifiedExecutableCacheFeatureSettingsTests
{
    private const string EnabledProperty = "CacheWorkflowExecutables";
    private const string CapacityProperty = "WorkflowExecutableCacheCapacity";

    [Fact]
    public void OriginalConnectionStringRegistrationOverloadIsPreserved()
    {
        var method = typeof(GroundworkSqliteUnifiedRegistration).GetMethod(
            nameof(GroundworkSqliteUnifiedRegistration.AddGroundworkSqliteUnifiedPersistence),
            [typeof(IServiceCollection), typeof(string)]);

        Assert.NotNull(method);
    }

    [Fact]
    public void OriginalConnectionStringRegistrationOverloadEnablesBoundedCacheByDefault()
    {
        var services = new ServiceCollection();
        services.AddGroundworkSqliteUnifiedPersistence("Data Source=:memory:");
        using var provider = services.BuildServiceProvider();

        Assert.True(provider.GetRequiredService<WorkflowExecutableCacheOptions>().Enabled);
    }

    [Fact]
    public void ExplicitRegistrationRejectsNullOptionsBeforeMutatingServices()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentNullException>(() =>
            services.AddGroundworkSqliteUnifiedPersistence(
                "Data Source=:memory:",
                (WorkflowExecutableCacheOptions)null!));
        Assert.Empty(services);
    }

    [Fact]
    public void SettingsArePublicManifestSettingsWithDurableDefaults()
    {
        var feature = NewFeature();

        AssertSetting(feature, EnabledProperty, true);
        AssertSetting(feature, CapacityProperty, 256);

        // The access-bound store cache sized the v1 document-store adapters. The v2 substrate binds access
        // per session, so those two settings went with the substrate rather than being carried forward.
        Assert.Null(feature.GetType().GetProperty("ReuseAccessBoundStores"));
        Assert.Null(feature.GetType().GetProperty("AccessBoundStoreCacheCapacity"));
    }

    [Fact]
    public void ConfiguredCapacityIsThreadedToUnifiedRuntimeRegistration()
    {
        var feature = NewFeature();
        SetSetting(feature, EnabledProperty, true);
        SetSetting(feature, CapacityProperty, 23);
        var services = new ServiceCollection();
        feature.ConfigureServices(services);
        using var provider = services.BuildServiceProvider();

        Assert.Equal(23, provider.GetRequiredService<WorkflowExecutableCacheOptions>().Capacity);
    }

    private static SqliteGroundworkUnifiedPersistenceShellFeature NewFeature() =>
        new(new ShellFeatureContext(
            new ShellSettings { Id = new ShellId("sqlite-cache-settings") },
            []));

    private static void AssertSetting(object feature, string name, object expected)
    {
        var property = feature.GetType().GetProperty(name);
        Assert.NotNull(property);
        Assert.Equal(expected, property!.GetValue(feature));
        Assert.Contains(property.CustomAttributes, attribute => attribute.AttributeType.Name == "ManifestSettingAttribute");
    }

    private static void SetSetting(object feature, string name, object value)
    {
        var property = feature.GetType().GetProperty(name);
        Assert.NotNull(property);
        property!.SetValue(feature, value);
    }
}
