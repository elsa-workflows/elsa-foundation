using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.Targets;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Persistence.Groundwork.Composition.Tests;

/// <summary>
/// A shell gives no guarantee that the provider feature configures before the feature holding the target
/// list, so neither side may assume the other has run. These pin the ordering-independence that makes the
/// provider-neutral target registry workable, and the diagnostics for the ways it can be misconfigured.
/// </summary>
public sealed class GroundworkTargetCompositionTests
{
    private sealed class RecordingLeaf(string providerIdentity) : IGroundworkProviderLeaf
    {
        public string ProviderIdentity { get; } = providerIdentity;

        public List<(string Target, GroundworkTargetEntry Entry)> Declared { get; } = [];

        public void DeclareTarget(IServiceCollection services, string targetName, GroundworkTargetEntry entry) =>
            Declared.Add((targetName, entry));
    }

    private static GroundworkTargetEntry Entry(string provider, string connection = "Data Source=:memory:") =>
        new() { Provider = provider, ConnectionString = connection };

    [Fact]
    public void A_target_configured_before_its_provider_leaf_is_opened_when_the_leaf_arrives()
    {
        var services = new ServiceCollection();
        var leaf = new RecordingLeaf("sqlite");

        services.AddGroundworkTargetConfiguration("authoring", Entry("sqlite"));
        services.AddGroundworkProviderLeaf(leaf);

        Assert.Equal("authoring", Assert.Single(leaf.Declared).Target);
        Assert.Empty(services.GroundworkTargetComposition().PendingTargets);
    }

    [Fact]
    public void A_target_configured_after_its_provider_leaf_is_opened_immediately()
    {
        var services = new ServiceCollection();
        var leaf = new RecordingLeaf("sqlite");

        services.AddGroundworkProviderLeaf(leaf);
        services.AddGroundworkTargetConfiguration("authoring", Entry("sqlite"));

        Assert.Equal("authoring", Assert.Single(leaf.Declared).Target);
        Assert.Empty(services.GroundworkTargetComposition().PendingTargets);
    }

    [Fact]
    public void Each_target_is_opened_exactly_once_even_when_a_leaf_is_composed_twice()
    {
        var services = new ServiceCollection();
        var leaf = new RecordingLeaf("sqlite");

        services.AddGroundworkTargetConfiguration("authoring", Entry("sqlite"));
        services.AddGroundworkProviderLeaf(leaf);
        services.AddGroundworkProviderLeaf(new RecordingLeaf("sqlite"));

        Assert.Single(leaf.Declared);
    }

    [Fact]
    public void Targets_are_routed_to_the_provider_each_one_names()
    {
        var services = new ServiceCollection();
        var sqlite = new RecordingLeaf("sqlite");
        var postgres = new RecordingLeaf("postgresql");

        services.AddGroundworkTargetConfiguration("runtime", Entry("sqlite"));
        services.AddGroundworkTargetConfiguration("authoring", Entry("postgresql", "Host=pg"));
        services.AddGroundworkProviderLeaf(sqlite);
        services.AddGroundworkProviderLeaf(postgres);

        Assert.Equal("runtime", Assert.Single(sqlite.Declared).Target);
        Assert.Equal("authoring", Assert.Single(postgres.Declared).Target);
    }

    [Fact]
    public void A_target_naming_an_uncomposed_provider_stays_pending_rather_than_doing_nothing_quietly()
    {
        var services = new ServiceCollection();
        services.AddGroundworkProviderLeaf(new RecordingLeaf("sqlite"));

        services.AddGroundworkTargetConfiguration("authoring", Entry("postgresql", "Host=pg"));

        var composition = services.GroundworkTargetComposition();
        Assert.Equal(["authoring"], composition.PendingTargets);
        Assert.Equal("postgresql", composition.RequestedProvider("authoring"));
        Assert.Equal(["sqlite"], composition.ComposedProviders);
    }

    [Fact]
    public void A_target_without_a_provider_is_a_configuration_error()
    {
        var services = new ServiceCollection();

        var failure = Assert.Throws<GroundworkTargetConfigurationException>(() =>
            services.AddGroundworkTargetConfiguration("authoring", new GroundworkTargetEntry()));

        Assert.Contains("does not name a provider", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_target_without_a_connection_string_is_a_configuration_error()
    {
        var entry = new GroundworkTargetEntry { Provider = "sqlite" };

        var failure = Assert.Throws<GroundworkTargetConfigurationException>(
            () => entry.RequireConnectionString("authoring"));

        Assert.Contains("'authoring'", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_unrecognized_provider_option_fails_rather_than_being_dropped()
    {
        var entry = Entry("sqlite");
        entry.Options["StoreCacheCapcity"] = "256";

        var failure = Assert.Throws<GroundworkTargetConfigurationException>(
            () => entry.EnsureOnlyKnownOptions("authoring", "StoreCacheCapacity"));

        Assert.Contains("'StoreCacheCapcity'", failure.Message, StringComparison.Ordinal);
        Assert.Contains("'StoreCacheCapacity'", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_malformed_option_value_names_the_target_and_the_key()
    {
        var entry = Entry("sqlite");
        entry.Options["SkipSchemaInspectionWhenPlanUnchanged"] = "perhaps";

        var failure = Assert.Throws<GroundworkTargetConfigurationException>(
            () => entry.GetBoolean("authoring", "SkipSchemaInspectionWhenPlanUnchanged", false));

        Assert.Contains("'authoring'", failure.Message, StringComparison.Ordinal);
        Assert.Contains("'perhaps'", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Two_leaves_claiming_one_provider_identity_is_a_composition_error()
    {
        var services = new ServiceCollection();
        services.AddGroundworkProviderLeaf(new RecordingLeaf("sqlite"));

        var conflict = Assert.Throws<GroundworkTargetConflictException>(
            () => services.AddGroundworkProviderLeaf(new OtherLeaf()));

        Assert.Contains("'sqlite'", conflict.Message, StringComparison.Ordinal);
    }

    private sealed class OtherLeaf : IGroundworkProviderLeaf
    {
        public string ProviderIdentity => "sqlite";

        public void DeclareTarget(IServiceCollection services, string targetName, GroundworkTargetEntry entry)
        {
        }
    }
}
