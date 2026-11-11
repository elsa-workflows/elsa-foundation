using Elsa.Activities.Graph.Runtime;
using Elsa.Activities.Primitives;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Extensions;
using Elsa.Activities.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Behavioral.Tests;

/// <summary>
/// Guards the capability registry against implementation-type collisions.
/// </summary>
public sealed class ActivityConsumerCapabilityCompositionTests
{
    [Fact]
    public void Both_activity_consumer_capabilities_survive_when_primitives_composes_first()
    {
        var capabilities = Compose(
            services => new ActivitiesPrimitivesFeature().ConfigureServices(services),
            services => new GraphActivitiesRuntimeFeature().ConfigureServices(services));

        AssertBothConsumersAdvertised(capabilities);
    }

    [Fact]
    public void Both_activity_consumer_capabilities_survive_when_graph_composes_first()
    {
        var capabilities = Compose(
            services => new GraphActivitiesRuntimeFeature().ConfigureServices(services),
            services => new ActivitiesPrimitivesFeature().ConfigureServices(services));

        AssertBothConsumersAdvertised(capabilities);
    }

    [Fact]
    public void Composing_a_feature_twice_still_advertises_its_capability_once()
    {
        var capabilities = Compose(
            services => new ActivitiesPrimitivesFeature().ConfigureServices(services),
            services => new GraphActivitiesRuntimeFeature().ConfigureServices(services),
            services => new ActivitiesPrimitivesFeature().ConfigureServices(services),
            services => new GraphActivitiesRuntimeFeature().ConfigureServices(services));

        AssertBothConsumersAdvertised(capabilities);
        Assert.Equal(2, capabilities.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_third_consumer_can_share_the_capability_record_in_either_composition_order(bool thirdFirst)
    {
        Action<IServiceCollection> third = services =>
            services.AddRuntimeActivityConsumerCapability("sample.third", ["1"]);
        Action<IServiceCollection> primitives = services => new ActivitiesPrimitivesFeature().ConfigureServices(services);
        Action<IServiceCollection> graph = services => new GraphActivitiesRuntimeFeature().ConfigureServices(services);
        var capabilities = thirdFirst ? Compose(third, graph, primitives) : Compose(primitives, graph, third);

        AssertBothConsumersAdvertised(capabilities);
        Assert.Equal("1", Assert.Single(Assert.Single(capabilities, capability => capability.ConsumerKey == "sample.third").SupportedSchemaVersions));
        Assert.Equal(3, capabilities.Count);
    }

    private static void AssertBothConsumersAdvertised(IReadOnlyCollection<IRuntimeActivityConsumerCapability> capabilities)
    {
        var clr = Assert.Single(capabilities, capability => capability.ConsumerKey == WellKnownRuntimeActivityConsumers.ClrActivity);
        var graph = Assert.Single(capabilities, capability => capability.ConsumerKey == WellKnownRuntimeActivityConsumers.GraphActivity);

        Assert.Contains(RuntimeActivityDescriptor.InitialSchemaVersion, clr.SupportedSchemaVersions);
        Assert.Contains(RuntimeActivityDescriptor.InitialSchemaVersion, graph.SupportedSchemaVersions);
    }

    private static IReadOnlyList<IRuntimeActivityConsumerCapability> Compose(params Action<IServiceCollection>[] features)
    {
        var services = new ServiceCollection();
        foreach (var feature in features)
            feature(services);
        using var provider = services.BuildServiceProvider();
        return provider.GetServices<IRuntimeActivityConsumerCapability>().ToList();
    }
}
