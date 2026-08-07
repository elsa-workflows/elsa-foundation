using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.Targets;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Persistence.Groundwork.Composition.Tests;

/// <summary>
/// The cross-lane store accessor has to be resolvable wherever lanes are composed.
/// <para>
/// It was consumed by both reusable-activity publication commands while being registered nowhere, so a
/// host that composed publishing failed at startup with "Unable to resolve service for type
/// GroundworkLaneStores". No unit test caught it because none resolved those commands from a container --
/// they are constructed directly in tests -- so the first thing to notice was the server refusing to
/// start. These assert the registration itself, which is the part that was missing.
/// </para>
/// </summary>
public sealed class GroundworkLaneStoreAccessorRegistrationTests
{
    [Fact]
    public void Composing_lanes_publishes_the_cross_lane_store_accessor()
    {
        var services = new ServiceCollection();

        services.GroundworkManifestBindings();

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(GroundworkLaneStores));
    }

    [Fact]
    public void The_cross_lane_store_accessor_resolves_from_a_scope()
    {
        var services = new ServiceCollection();
        services.GroundworkManifestBindings();

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            // The failure this guards against surfaced under a host that validates scopes, so validate
            // here too: a singleton accessor holding the root provider would be caught rather than
            // silently capturing the scoped per-target stores it hands out.
            ValidateScopes = true
        });
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetRequiredService<GroundworkLaneStores>());
    }

    [Fact]
    public void The_cross_lane_store_accessor_is_scoped()
    {
        var services = new ServiceCollection();
        services.GroundworkManifestBindings();

        var descriptor = Assert.Single(
            services,
            candidate => candidate.ServiceType == typeof(GroundworkLaneStores));

        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void Composing_lanes_twice_publishes_one_accessor()
    {
        var services = new ServiceCollection();

        services.GroundworkManifestBindings();
        services.GroundworkManifestBindings();

        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(GroundworkLaneStores));
    }
}
