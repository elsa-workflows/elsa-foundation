using Elsa.Expressions;
using Elsa.Expressions.Core.Contracts;
using Elsa.Serialization.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Elsa.Expressions.Tests.Unit;

/// <summary>
/// Registration tests: the new type-descriptor provider and the registry-seeding startup task
/// are wired by their owning features and resolve from DI.
/// </summary>
public sealed class FeatureRegistrationTests
{
    [Fact]
    public void Json_descriptor_seeds_both_json_and_nullable_json_resolution()
    {
        var registry = TestRegistry.SeededWithPrimitives();
        Assert.True(registry.TryGetType("System.Text.Json.JsonElement", out var json));
        Assert.Equal(typeof(System.Text.Json.JsonElement), json);
        Assert.True(registry.TryGetType("System.Text.Json.JsonElement?", out var nullable));
        Assert.Equal(typeof(System.Text.Json.JsonElement?), nullable);
    }

    private static ServiceCollection MinimalServices()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        return services;
    }

    [Fact]
    public void ExpressionsFeature_RegistersVariableTypeDescriptorCatalog()
    {
        var services = MinimalServices();

        new ExpressionsFeature().ConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        var catalog = provider.GetRequiredService<IVariableTypeDescriptorCatalog>();

        // Resolvability + behaviour, not concrete-type pinning of the catalog implementation.
        Assert.Contains(catalog.GetDescriptors(), d => d.Alias == "String");
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
