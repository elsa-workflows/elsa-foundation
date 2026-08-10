using Elsa.Persistence.Groundwork.Targets;
using Elsa.Persistence.Groundwork.Unified.Composition;
using Elsa.Persistence.Groundwork.Unified.DependencyInjection;
using Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Persistence.Groundwork.Composition.Tests;

/// <summary>
/// Capability admission became per-target in this stage: a provider leaf publishes it keyed by target, and
/// additionally unkeyed for the default target so that target-unaware consumers keep resolving it.
/// <para>
/// The unkeyed publication is an <em>alias</em>, so it must resolve to the very instance provider admission
/// fills. Any consumer that registers the concrete type unkeyed on its own would race that alias through
/// TryAdd and, if it won, hand target-unaware consumers an empty snapshot — capabilities reported
/// unavailable after a successful admission, with nothing raised. These assert the alias identity in both
/// registration orders, because the order is what decides a TryAdd race.
/// </para>
/// </summary>
public sealed class GroundworkCapabilityAdmissionPublicationTests
{
    [Fact]
    public void The_unkeyed_default_admission_is_the_keyed_default_instance()
    {
        var services = new ServiceCollection();

        services.AddGroundworkProviderCapabilityAdmission();

        AssertUnkeyedAliasesDefault(services);
    }

    [Fact]
    public void A_consumer_registered_before_the_provider_leaf_does_not_shadow_the_alias()
    {
        var services = new ServiceCollection();

        // The losing order for a TryAdd race: the consumer composes first.
        services.AddGroundworkDistributedRuntimeStores();
        services.AddGroundworkProviderCapabilityAdmission();

        AssertUnkeyedAliasesDefault(services);
    }

    [Fact]
    public void A_consumer_registered_after_the_provider_leaf_does_not_shadow_the_alias()
    {
        var services = new ServiceCollection();

        services.AddGroundworkProviderCapabilityAdmission();
        services.AddGroundworkDistributedRuntimeStores();

        AssertUnkeyedAliasesDefault(services);
    }

    [Fact]
    public void A_non_default_target_is_not_published_unkeyed()
    {
        var services = new ServiceCollection();

        services.AddGroundworkProviderCapabilityAdmission("authoring");

        using var provider = services.BuildServiceProvider();
        Assert.NotNull(provider.GetRequiredKeyedService<GroundworkProviderCapabilityAdmission>("authoring"));
        Assert.Null(provider.GetService<GroundworkProviderCapabilityAdmission>());
    }

    [Fact]
    public void Each_target_keeps_its_own_admission_snapshot()
    {
        var services = new ServiceCollection();

        services.AddGroundworkProviderCapabilityAdmission();
        services.AddGroundworkProviderCapabilityAdmission("authoring");

        using var provider = services.BuildServiceProvider();
        Assert.NotSame(
            provider.GetRequiredKeyedService<GroundworkProviderCapabilityAdmission>(GroundworkTargetNames.Default),
            provider.GetRequiredKeyedService<GroundworkProviderCapabilityAdmission>("authoring"));
    }

    /// <summary>
    /// Both resolutions come from one built provider — across two providers, singleton identity would mean
    /// nothing and the assertion would pass or fail for the wrong reason.
    /// </summary>
    private static void AssertUnkeyedAliasesDefault(IServiceCollection services)
    {
        using var provider = services.BuildServiceProvider();

        Assert.Same(
            provider.GetRequiredKeyedService<GroundworkProviderCapabilityAdmission>(GroundworkTargetNames.Default),
            provider.GetRequiredService<GroundworkProviderCapabilityAdmission>());
    }
}
