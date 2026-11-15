using Elsa.Activities.Graph.Runtime;
using Elsa.Activities.Primitives;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Behavioral.Tests;

/// <summary>
/// Guards the capability registry against implementation-type collisions.
/// </summary>
/// <remarks>
/// Capabilities are registered with <c>TryAddEnumerable</c>, which de-duplicates by implementation type.
/// When two features registered instances of the same class, whichever composed second was silently dropped
/// and the runtime refused artifacts it could execute. These cases compose <em>both</em> features — a container
/// holding only one of them passes with or without the fix — and do so in both registration orders, because
/// which capability loses a collision depends on composition order, which CShells does not guarantee.
/// </remarks>
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

    [Fact]
    public void Every_registered_capability_has_its_own_implementation_type()
    {
        var capabilities = Compose(
            services => new ActivitiesPrimitivesFeature().ConfigureServices(services),
            services => new GraphActivitiesRuntimeFeature().ConfigureServices(services));

        var distinctTypes = capabilities.Select(capability => capability.GetType()).Distinct().Count();

        Assert.Equal(capabilities.Count, distinctTypes);
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
        return services.BuildServiceProvider().GetServices<IRuntimeActivityConsumerCapability>().ToList();
    }
}
