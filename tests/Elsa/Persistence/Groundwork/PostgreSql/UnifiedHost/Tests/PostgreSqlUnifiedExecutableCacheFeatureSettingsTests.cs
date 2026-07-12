using Elsa.Persistence.Groundwork.PostgreSql.Unified;
using Elsa.Workflows.Runtime.Core.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Persistence.Groundwork.PostgreSql.UnifiedHost.Tests;

public sealed class PostgreSqlUnifiedExecutableCacheFeatureSettingsTests
{
    private const string EnabledProperty = "CacheWorkflowExecutables";
    private const string CapacityProperty = "WorkflowExecutableCacheCapacity";
    private const string OptionsTypeName = "Elsa.Workflows.Runtime.Core.Models.WorkflowExecutableCacheOptions";

    [Fact]
    public void SettingsArePublicManifestSettingsWithDurableDefaults()
    {
        var feature = new PostgreSqlGroundworkUnifiedPersistenceShellFeature();

        AssertSetting(feature, EnabledProperty, true);
        AssertSetting(feature, CapacityProperty, 256);
    }

    [Fact]
    public void ConfiguredCapacityIsThreadedToUnifiedRuntimeRegistration()
    {
        var feature = new PostgreSqlGroundworkUnifiedPersistenceShellFeature();
        SetSetting(feature, EnabledProperty, true);
        SetSetting(feature, CapacityProperty, 29);
        var services = new ServiceCollection();
        feature.ConfigureServices(services);
        using var provider = services.BuildServiceProvider();

        Assert.Equal(29, ResolveCapacity(provider));
    }

    private static int ResolveCapacity(IServiceProvider provider)
    {
        var optionsType = typeof(IWorkflowExecutableStore).Assembly.GetType(OptionsTypeName);
        Assert.NotNull(optionsType);
        var options = provider.GetService(optionsType!);
        Assert.NotNull(options);
        var capacity = options!.GetType().GetProperty("Capacity");
        Assert.NotNull(capacity);
        return Assert.IsType<int>(capacity!.GetValue(options));
    }

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
