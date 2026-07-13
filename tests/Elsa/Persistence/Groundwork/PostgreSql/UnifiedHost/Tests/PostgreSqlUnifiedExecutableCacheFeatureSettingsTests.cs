using Elsa.Persistence.Groundwork.PostgreSql.Unified;
using Elsa.Persistence.Groundwork.PostgreSql.Unified.DependencyInjection;
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
    public void OriginalConnectionStringRegistrationOverloadIsPreserved()
    {
        var method = typeof(GroundworkPostgreSqlUnifiedRegistration).GetMethod(
            nameof(GroundworkPostgreSqlUnifiedRegistration.AddGroundworkPostgreSqlUnifiedPersistence),
            [typeof(IServiceCollection), typeof(string)]);

        Assert.NotNull(method);
    }

    [Fact]
    public void OriginalConnectionStringRegistrationOverloadPreservesDirectReadBehavior()
    {
        var services = new ServiceCollection();
        services.AddGroundworkPostgreSqlUnifiedPersistence("Host=localhost;Database=elsa");
        using var provider = services.BuildServiceProvider();

        Assert.False(ResolveOption<bool>(provider, EnabledProperty));
    }

    [Fact]
    public void ExplicitRegistrationRejectsNullOptionsBeforeMutatingServices()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentNullException>(() =>
            services.AddGroundworkPostgreSqlUnifiedPersistence("Host=localhost;Database=elsa", null!));
        Assert.Empty(services);
    }

    [Fact]
    public void SettingsArePublicManifestSettingsWithDurableDefaults()
    {
        var feature = new PostgreSqlGroundworkUnifiedPersistenceShellFeature();

        AssertSetting(feature, EnabledProperty, false);
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

        Assert.Equal(29, ResolveOption<int>(provider, CapacityProperty));
    }

    private static T ResolveOption<T>(IServiceProvider provider, string propertyName)
    {
        var optionsType = typeof(IWorkflowExecutableStore).Assembly.GetType(OptionsTypeName);
        Assert.NotNull(optionsType);
        var options = provider.GetService(optionsType!);
        Assert.NotNull(options);
        var property = options!.GetType().GetProperty(propertyName);
        Assert.NotNull(property);
        return Assert.IsType<T>(property!.GetValue(options));
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
