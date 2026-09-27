using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Core.Exceptions;
using Elsa.Cluster.Core.Extensions;
using Elsa.Cluster.Core.Models;
using Elsa.Cluster.Core.Options;
using Elsa.Cluster.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Elsa.Cluster.Tests.Core;

public sealed class ClusterMembershipRegistrationTests
{
    private readonly ServiceCollection _services = new();

    [Fact]
    public void A_second_durable_provider_is_refused_naming_both()
    {
        _services.AddClusterMembershipProvider(Durable("first"));

        var refusal = Assert.Throws<ClusterMembershipConfigurationException>(() => _services.AddClusterMembershipProvider(Durable("second")));

        Assert.Contains("'first'", refusal.Message, StringComparison.Ordinal);
        Assert.Contains("'second'", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_durable_provider_is_refused_beside_a_membership_registered_directly()
    {
        ConformanceSentinelProvider.ComposeDirectly(_services);

        var refusal = Assert.Throws<ClusterMembershipConfigurationException>(() => _services.AddClusterMembershipProvider(Durable("durable")));

        Assert.Contains(nameof(ConformanceSentinelProvider.InertMembership), refusal.Message, StringComparison.Ordinal);
        Assert.Contains("'durable'", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_default_yields_to_a_durable_provider_composed_after_it()
    {
        var @default = Default();
        var durable = Durable("durable");
        _services.TryAddClusterMembershipDefault(@default);

        _services.AddClusterMembershipProvider(durable);

        Assert.Same(durable.MembershipDescriptor, Assert.Single(_services, descriptor => descriptor.ServiceType == typeof(IClusterMembership)));
        Assert.Same(durable, Assert.Single(Registrations()));
    }

    [Fact]
    public void The_default_does_nothing_once_a_membership_is_registered()
    {
        var durable = Durable("durable");
        _services.AddClusterMembershipProvider(durable);
        var before = _services.Count;

        _services.TryAddClusterMembershipDefault(Default());

        Assert.Equal(before, _services.Count);
        Assert.Same(durable, Assert.Single(Registrations()));
    }

    [Fact]
    public void Each_kind_has_its_own_entry_point()
    {
        Assert.Throws<ArgumentException>(() => _services.AddClusterMembershipProvider(Default()));
        Assert.Throws<ArgumentException>(() => _services.TryAddClusterMembershipDefault(Durable("durable")));
    }

    [Fact]
    public void A_registration_owns_a_membership_descriptor_only() =>
        Assert.Throws<ArgumentException>(() => new ClusterMembershipProviderRegistration(
            "wrong", ClusterProviderKind.Durable, ServiceDescriptor.Singleton<TimeProvider>(TimeProvider.System)));

    [Fact]
    public void The_default_starts_without_a_host_id() =>
        Assert.Null(Settings(compose: services => services.TryAddClusterMembershipDefault(Default())).HostId);

    [Fact]
    public void A_durable_provider_without_an_explicit_host_id_is_refused_naming_it()
    {
        var failure = Refusal(compose: services => services.AddClusterMembershipProvider(Durable("durable")));

        Assert.Contains("'durable'", failure, StringComparison.Ordinal);
        Assert.Contains($"{ClusterMembershipOptions.SectionName}:HostId", failure, StringComparison.Ordinal);
    }

    [Fact]
    public void A_durable_provider_with_an_explicit_host_id_passes() =>
        Assert.Equal("web-1", Settings(options => options.HostId = "web-1", services => services.AddClusterMembershipProvider(Durable("durable"))).HostId);

    [Fact]
    public void A_membership_registered_directly_after_the_provider_is_refused_naming_both()
    {
        var failure = Refusal(compose: services =>
        {
            services.TryAddClusterMembershipDefault(Default());
            ConformanceSentinelProvider.ComposeDirectly(services);
        });

        Assert.Contains("'default'", failure, StringComparison.Ordinal);
        Assert.Contains(nameof(ConformanceSentinelProvider.InertMembership), failure, StringComparison.Ordinal);
    }

    [Fact]
    public void A_provider_registration_removed_after_composition_is_refused() =>
        Assert.Contains("removed", Refusal(compose: services =>
        {
            services.TryAddClusterMembershipDefault(Default());
            services.Remove(services.Single(descriptor => descriptor.ServiceType == typeof(ClusterMembershipProviderRegistration)));
        }), StringComparison.Ordinal);

    public static TheoryData<string, Action<ClusterMembershipOptions>> InvalidSettings => new()
    {
        { "HostId", options => options.HostId = " " },
        { "HostId", options => options.HostId = new string('h', ClusterHostIdConstraints.MaximumLength + 1) },
        { "HeartbeatInterval", options => options.HeartbeatInterval = TimeSpan.Zero },
        { "ExpiryPeriod", options => options.ExpiryPeriod = options.HeartbeatInterval * 3 - TimeSpan.FromTicks(1) },
        { "SkewAllowance", options => options.SkewAllowance = TimeSpan.FromTicks(-1) }
    };

    [Theory]
    [MemberData(nameof(InvalidSettings))]
    public void Invalid_settings_are_refused_naming_the_setting(string setting, Action<ClusterMembershipOptions> configure) =>
        Assert.Contains($"{ClusterMembershipOptions.SectionName}:{setting}", Refusal(configure, services => services.TryAddClusterMembershipDefault(Default())), StringComparison.Ordinal);

    [Fact]
    public void Three_heartbeat_intervals_is_the_shortest_expiry_period_accepted() =>
        Assert.NotNull(Settings(
            options => options.ExpiryPeriod = options.HeartbeatInterval * 3,
            services => services.TryAddClusterMembershipDefault(Default())));

    private ClusterMembershipProviderRegistration[] Registrations() =>
        _services.Select(descriptor => descriptor.ImplementationInstance).OfType<ClusterMembershipProviderRegistration>().ToArray();

    private ClusterMembershipOptions Settings(Action<ClusterMembershipOptions>? configure = null, Action<IServiceCollection>? compose = null)
    {
        if (configure is not null)
            _services.Configure(configure);
        compose?.Invoke(_services);
        using var provider = _services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<ClusterMembershipOptions>>().Value;
    }

    private string Refusal(Action<ClusterMembershipOptions>? configure = null, Action<IServiceCollection>? compose = null) =>
        string.Join(Environment.NewLine, Assert.Throws<OptionsValidationException>(() => Settings(configure, compose)).Failures);

    private static ClusterMembershipProviderRegistration Durable(string name) =>
        new(name, ClusterProviderKind.Durable, ServiceDescriptor.Singleton<IClusterMembership, ConformanceSentinelProvider.InertMembership>());

    private static ClusterMembershipProviderRegistration Default() =>
        new("default", ClusterProviderKind.InProcess, ServiceDescriptor.Singleton<IClusterMembership, ConformanceSentinelProvider.InertMembership>());
}
