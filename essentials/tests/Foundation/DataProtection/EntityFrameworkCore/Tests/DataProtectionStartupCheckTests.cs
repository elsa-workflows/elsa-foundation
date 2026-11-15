using Elsa.Cluster.Core.Options;
using Elsa.Cluster.EntityFrameworkCore;
using Elsa.Cluster.Hosting;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Elsa.Foundation.DataProtection.EntityFrameworkCore.Tests;

/// <summary>
/// The two compositions whose failure looks like success are warned about as the host starts (#2191): a clustered host whose
/// key ring is its own, and a shared key store without a certificate. Each is checked both ways, so a warning that fires on
/// every host, which is as useless as one that never fires, fails too.
/// </summary>
public sealed class DataProtectionStartupCheckTests : IAsyncLifetime
{
    private const string Check = "Elsa.Foundation.DataProtection.DataProtectionStartupCheck";
    private const string NotShared = "key ring is its own";
    private const string Unencrypted = "stored unencrypted";
    private readonly SqliteKeyRingDatabase _database = new();
    private readonly List<IAsyncDisposable> _hosts = [];
    private readonly TestCertificate _certificate = TestCertificate.Create();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var host in _hosts)
            await host.DisposeAsync();
        _certificate.Dispose();
        _database.Dispose();
    }

    [Fact]
    public async Task A_clustered_host_without_a_shared_key_store_is_warned_about()
    {
        var host = await StartAsync(await ClusteredAsync());

        var warning = Assert.Single(host.Log.From(Check, LogLevel.Warning));
        Assert.Contains(NotShared, warning, StringComparison.Ordinal);
        Assert.Contains($"{KeyRingStore.KeyStoreSection}:{DataProtectionConfigurationExtensions.EnabledKey}=true", warning, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_clustered_host_with_the_shared_key_store_is_not_warned_about()
    {
        var host = await StartAsync(With(await ClusteredAsync(), (await _database.CreateStoreAsync()).Settings(), _certificate.Settings()));

        Assert.Empty(host.Log.From(Check, LogLevel.Warning));
    }

    /// <summary>A cluster of one, the default of every host, keeps its keys to itself and has nothing to share them with.</summary>
    [Fact]
    public async Task A_host_that_is_not_clustered_is_not_warned_about()
    {
        var host = await StartAsync(new());

        Assert.Empty(host.Log.From(Check, LogLevel.Warning));
    }

    [Fact]
    public async Task A_key_store_without_a_certificate_is_warned_about()
    {
        var host = await StartAsync((await _database.CreateStoreAsync()).Settings());

        var warning = Assert.Single(host.Log.From(Check, LogLevel.Warning));
        Assert.Contains(Unencrypted, warning, StringComparison.Ordinal);
        Assert.Contains(DataProtectionKeysEfModule.TableName, warning, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_key_store_with_a_certificate_is_not_warned_about()
    {
        var host = await StartAsync(With((await _database.CreateStoreAsync()).Settings(), _certificate.Settings()));

        Assert.Empty(host.Log.From(Check, LogLevel.Warning));
    }

    /// <summary>The durable EF membership provider, enabled from configuration as a clustered host enables it.</summary>
    private async Task<Dictionary<string, string?>> ClusteredAsync()
    {
        const string membership = ClusterMembershipOptions.SectionName;
        const string ef = $"{membership}:{EfClusterMembershipOptions.SectionKey}";
        var store = await _database.CreateStoreAsync();
        return new()
        {
            [$"{membership}:HostId"] = "data-protection-check",
            [$"{ef}:Enabled"] = "true",
            [$"{ef}:Provider"] = store.Provider,
            [$"{ef}:ConnectionString"] = store.ConnectionString
        };
    }

    /// <summary>
    /// Starts a host with cluster membership composed from <paramref name="settings"/>. A host without the key store keeps its
    /// keys where ASP.NET Core keeps them by default, the home directory of whoever runs these tests, so Data Protection's own
    /// hosted service, which would create a key there as the host starts, is left out of it; the check does not need it.
    /// </summary>
    private async Task<KeyRingHost> StartAsync(Dictionary<string, string?> settings)
    {
        var keyStore = settings.ContainsKey($"{KeyRingStore.KeyStoreSection}:{DataProtectionConfigurationExtensions.EnabledKey}");
        var host = await KeyRingHost.StartAsync(settings, (services, configuration) =>
        {
            services.AddConfiguredClusterMembership(configuration);
            if (keyStore)
                return;
            foreach (var loader in services.Where(descriptor => descriptor.ServiceType == typeof(IHostedService) && descriptor.ImplementationType?.Assembly == typeof(KeyManagementOptions).Assembly).ToArray())
                services.Remove(loader);
        });
        _hosts.Add(host);
        return host;
    }

    private static Dictionary<string, string?> With(params Dictionary<string, string?>[] settings) =>
        settings.SelectMany(entries => entries).ToDictionary();
}
