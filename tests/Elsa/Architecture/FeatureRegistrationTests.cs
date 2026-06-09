using Elsa.Primitives.Contracts;
using Elsa.Primitives.Hosting;
using Elsa.Primitives.Hosting.Services;
using Elsa.Serialization.Core;
using Elsa.Serialization.Newtonsoft;
using Elsa.Serialization.Newtonsoft.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Architecture.Tests;

public sealed class FeatureRegistrationTests
{
    [Fact]
    public void Primitives_hosting_feature_registers_system_clock()
    {
        var services = new ServiceCollection();
        var feature = new PrimitivesFeature();

        feature.ConfigureServices(services);
        using var provider = services.BuildServiceProvider();

        Assert.IsType<SystemClock>(provider.GetRequiredService<ISystemClock>());
    }

    [Fact]
    public void Newtonsoft_serialization_feature_registers_json_island_handler()
    {
        var services = new ServiceCollection();
        var feature = new NewtonsoftSerializationFeature();

        feature.ConfigureServices(services);
        using var provider = services.BuildServiceProvider();

        Assert.IsType<NewtonsoftJsonIslandTypeHandler>(provider.GetRequiredService<IJsonIslandTypeHandler>());
    }
}
