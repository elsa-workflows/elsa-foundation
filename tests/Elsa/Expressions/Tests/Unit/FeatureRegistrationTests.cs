using Elsa.Expressions;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Services;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText;
using Elsa.Serialization.SystemText.Startup;
using Elsa.Tasks.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Elsa.Expressions.Tests.Unit;

/// <summary>
/// §2.23.1 registration tests: the new type-descriptor provider and the registry-seeding startup task
/// are wired by their owning features and resolve from DI.
/// </summary>
public sealed class FeatureRegistrationTests
{
    private static ServiceCollection MinimalServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        return services;
    }

    [Fact]
    public void ExpressionsFeature_RegistersDefaultVariableTypeDescriptorProvider()
    {
        var services = MinimalServices();

        new ExpressionsFeature().ConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        var providers = provider.GetServices<IVariableTypeDescriptorProvider>().ToList();

        Assert.Contains(providers, p => p is DefaultVariableTypeDescriptorProvider);
    }

    [Fact]
    public void ExpressionsFeature_RegistersVariableTypeDescriptorCatalog()
    {
        var services = MinimalServices();

        new ExpressionsFeature().ConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        var catalog = provider.GetRequiredService<IVariableTypeDescriptorCatalog>();

        Assert.IsType<VariableTypeDescriptorCatalog>(catalog);
        Assert.Contains(catalog.GetDescriptors(), d => d.Alias == "String");
    }

    [Fact]
    public void SerializationFeature_RegistersSeedWellKnownTypesStartupTask()
    {
        var services = MinimalServices();

        new SerializationFeature().ConfigureServices(services);

        Assert.Contains(services, d => d.ServiceType == typeof(IStartupTask)
            && d.ImplementationType == typeof(SeedWellKnownTypesStartupTask));
    }

    [Fact]
    public void DefaultProvider_Seeds_RegistryWithFrameworkPrimitiveAliases()
    {
        var registry = TestRegistry.SeededWithPrimitives();

        Assert.True(registry.TryGetType("String", out var stringType));
        Assert.Equal(typeof(string), stringType);
        Assert.True(registry.TryGetType("Int32", out var intType));
        Assert.Equal(typeof(int), intType);
        Assert.True(registry.TryGetType("Boolean", out var boolType));
        Assert.Equal(typeof(bool), boolType);
    }
}
