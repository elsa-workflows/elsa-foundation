using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Core.Exceptions;
using Elsa.Cluster.Core.Models;
using Elsa.Cluster.Core.Options;
using Elsa.Cluster.EntityFrameworkCore.Testing;
using Elsa.Cluster.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Elsa.Cluster.EntityFrameworkCore.Tests;

/// <summary>
/// The EF provider is enabled only by configuration, and the default stays in-process (#2098, Acceptance; spec 183,
/// FR-018a, FR-024). A configuration the host cannot act on as written is refused, never quietly defaulted.
/// </summary>
public sealed class ClusterMembershipConfigurationTests : IDisposable
{
    private const string Section = ClusterMembershipOptions.SectionName;
    private const string Ef = Section + ":" + EfClusterMembershipOptions.SectionKey;
    private readonly EfClusterMembershipTestStore _store = EfClusterMembershipTestStore.CreateSqlite();

    public void Dispose() => _store.Dispose();

    [Theory]
    [InlineData(null)]
    [InlineData("false")]
    public void Nothing_is_registered_unless_configuration_enables_the_provider(string? enabled)
    {
        var settings = new Dictionary<string, string?> { [$"{Section}:HostId"] = "configured" };
        if (enabled is not null)
            settings[$"{Ef}:Enabled"] = enabled;
        var services = new ServiceCollection();

        services.AddConfiguredClusterMembership(Configuration(settings));

        Assert.Empty(services);
    }

    /// <summary>A host that meant to join a cluster and silently stayed a cluster of one is the failure that looks like success.</summary>
    [Fact]
    public void Provider_settings_without_an_Enabled_switch_are_refused()
    {
        var failure = Assert.Throws<ClusterMembershipConfigurationException>(() =>
            new ServiceCollection().AddConfiguredClusterMembership(Configuration(new() { [$"{Ef}:Provider"] = "SqlServer" })));

        Assert.Contains($"{Ef}:Enabled", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Enabled", "yes")]
    [InlineData("Pooling", "sometimes")]
    [InlineData("CleanupPeriod", "ten minutes")]
    public void A_provider_value_that_does_not_parse_is_refused_naming_its_key(string key, string value)
    {
        var settings = new Dictionary<string, string?> { [$"{Ef}:Enabled"] = "true", [$"{Ef}:{key}"] = value };

        var failure = Assert.Throws<ClusterMembershipConfigurationException>(() => new ServiceCollection().AddConfiguredClusterMembership(Configuration(settings)));

        Assert.Contains($"{Ef}:{key}", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_timing_that_does_not_parse_is_refused_naming_its_key()
    {
        var settings = new Dictionary<string, string?> { [$"{Ef}:Enabled"] = "true", [$"{Section}:HeartbeatInterval"] = "every ten seconds" };

        var failure = Assert.Throws<ClusterMembershipConfigurationException>(() => new ServiceCollection().AddConfiguredClusterMembership(Configuration(settings)));

        Assert.Contains($"{Section}:HeartbeatInterval", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Enabling_the_provider_composes_it_with_the_configured_host_id_timings_and_store()
    {
        using var host = BuildHost(new()
        {
            [$"{Section}:HostId"] = "configured-host",
            [$"{Section}:HeartbeatInterval"] = "00:00:02",
            [$"{Section}:ExpiryPeriod"] = "00:00:06",
            [$"{Section}:SkewAllowance"] = "00:00:01",
            [$"{Ef}:Enabled"] = "true",
            [$"{Ef}:Provider"] = _store.Provider,
            [$"{Ef}:ConnectionString"] = _store.ConnectionString,
            [$"{Ef}:CleanupPeriod"] = "00:01:00"
        });
        await host.StartAsync();

        var membership = host.Services.GetRequiredService<IClusterMembership>();
        Assert.IsType<EfClusterMembership>(membership);
        Assert.Equal(EfClusterMembershipServiceCollectionExtensions.ProviderName, Assert.Single(host.Services.GetServices<ClusterMembershipProviderRegistration>()).Name);
        var self = Assert.Single((await membership.ReadFleetAsync(FleetReadMode.Fresh)).Members);
        Assert.Equal("configured-host", self.HostId);
        Assert.Equal(TimeSpan.FromSeconds(6), self.ExpiryPeriod);
        Assert.Equal(TimeSpan.FromSeconds(2), host.Services.GetRequiredService<IOptions<ClusterMembershipOptions>>().Value.HeartbeatInterval);
        await host.StopAsync();
    }

    /// <summary>FR-003a through configuration: a durable provider without an explicit host id never starts.</summary>
    [Fact]
    public async Task An_enabled_provider_without_a_host_id_refuses_to_start()
    {
        using var host = BuildHost(new()
        {
            [$"{Ef}:Enabled"] = "true",
            [$"{Ef}:Provider"] = _store.Provider,
            [$"{Ef}:ConnectionString"] = _store.ConnectionString
        });

        var failure = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());

        Assert.Contains("HostId", failure.Message, StringComparison.Ordinal);
    }

    private static IConfiguration Configuration(Dictionary<string, string?> settings) =>
        new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

    private static IHost BuildHost(Dictionary<string, string?> settings)
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection(settings);
        builder.Services.AddConfiguredClusterMembership(builder.Configuration);
        return builder.Build();
    }
}
