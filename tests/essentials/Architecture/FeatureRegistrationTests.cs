using Elsa.Primitives.Contracts;
using Elsa.Primitives.Hosting;
using Elsa.Primitives.Hosting.Services;
using Elsa.Serialization.Core;
using Elsa.Serialization.Newtonsoft;
using Elsa.Serialization.Newtonsoft.Services;
using Elsa3.Mapping;
using Elsa3.Mapping.Contracts;
using Elsa3.Mapping.Services;
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

        // TS-1 (§2.23.1): the feature makes ISystemClock resolvable; resolvability replaces the concrete-type pin.
        provider.GetRequiredService<ISystemClock>();
    }

    [Fact]
    public void Newtonsoft_serialization_feature_registers_json_island_handler()
    {
        var services = new ServiceCollection();
        var feature = new NewtonsoftSerializationFeature();

        feature.ConfigureServices(services);
        using var provider = services.BuildServiceProvider();

        // TS-1 (§2.23.1): the feature makes IJsonIslandTypeHandler resolvable; resolvability replaces the concrete pin.
        provider.GetRequiredService<IJsonIslandTypeHandler>();
    }

    [Fact]
    public void Elsa3_mapping_feature_registers_workflow_definition_importer_boundary()
    {
        var services = new ServiceCollection();
        var feature = new Elsa3MappingFeature();

        feature.ConfigureServices(services);

        // TS-1 (§2.23.1): assert the importer boundary is registered, not its implementation type or lifetime.
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IElsa3WorkflowDefinitionImporter));
    }
}
