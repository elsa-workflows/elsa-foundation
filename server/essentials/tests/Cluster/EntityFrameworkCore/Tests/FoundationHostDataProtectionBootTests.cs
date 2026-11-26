using Elsa.Cluster.Core.Options;
using Elsa.Cluster.Hosting;
using static Elsa.Cluster.EntityFrameworkCore.Tests.FoundationHostComposition;

namespace Elsa.Cluster.EntityFrameworkCore.Tests;

/// <summary>
/// #2191 on the real thing: the built <c>Elsa.Foundation.Host</c>, clustered through the durable EF membership provider from
/// configuration alone, says as it starts when its Data Protection key ring is its own, since nothing else enforces sharing
/// it: a host that does not share it starts and serves, and refuses only the cookies and antiforgery tokens another host
/// issued. The same host sharing the key ring says nothing, so the warning is not one every clustered host logs.
/// </summary>
[Collection(FoundationHostCollection.Name)]
public sealed class FoundationHostDataProtectionBootTests(FoundationHostFeed feed) : IAsyncLifetime
{
    /// <summary>What the host's startup check says, restated: nothing of the host is loaded into this process.</summary>
    private const string NotShared = "Data Protection key ring is its own";

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private readonly string _file = Path.Join(Path.GetTempPath(), $"elsa-foundation-host-keys-{Guid.NewGuid():N}.db");
    private readonly List<FoundationHostProcess> _hosts = [];

    private string ConnectionString => $"Data Source={_file};Pooling=False";

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var host in _hosts)
            await host.DisposeAsync();
        foreach (var file in new[] { _file, _file + "-journal", _file + "-wal", _file + "-shm" })
            File.Delete(file);
    }

    [Fact]
    public async Task A_clustered_host_without_the_shared_key_store_warns_as_it_starts()
    {
        var host = await StartAsync(Clustered("foundation-host-keys-alone"));

        Assert.Contains(NotShared, await StartedOutputAsync(host), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_clustered_host_with_the_shared_key_store_does_not()
    {
        var settings = Clustered("foundation-host-keys-shared");
        settings["Elsa:DataProtection:EntityFrameworkCore:Enabled"] = "true";
        settings["Elsa:DataProtection:EntityFrameworkCore:Provider"] = "Sqlite";
        settings["Elsa:DataProtection:EntityFrameworkCore:ConnectionString"] = ConnectionString;

        var host = await StartAsync(settings);

        Assert.DoesNotContain(NotShared, await StartedOutputAsync(host), StringComparison.Ordinal);
    }

    /// <summary>The durable EF membership provider enabled under <paramref name="hostId"/>, over the test's database.</summary>
    private Dictionary<string, string> Clustered(string hostId)
    {
        const string ef = $"{ClusterMembershipOptions.SectionName}:{EfClusterMembershipOptions.SectionKey}";
        var settings = Settings(feed);
        settings[$"{ClusterMembershipOptions.SectionName}:{nameof(ClusterMembershipOptions.HostId)}"] = hostId;
        settings[$"{ef}:{ClusterMembershipConfigurationExtensions.EnabledKey}"] = "true";
        settings[$"{ef}:{nameof(EfClusterMembershipOptions.Provider)}"] = "Sqlite";
        settings[$"{ef}:{nameof(EfClusterMembershipOptions.ConnectionString)}"] = ConnectionString;
        return settings;
    }

    /// <summary>A host with no package and a shell that enables nothing: membership and Data Protection are the host's own.</summary>
    private async Task<FoundationHostProcess> StartAsync(Dictionary<string, string> settings)
    {
        var host = await FoundationHostProcess.StartAsync(Shells(ConnectionString), Array.Empty<string>(), settings);
        _hosts.Add(host);
        return host;
    }

    /// <summary>
    /// The host's output once it reports itself started, which it does after every hosted service has started, the startup
    /// check among them, so a warning the check logged is in the output by then.
    /// </summary>
    private static async Task<string> StartedOutputAsync(FoundationHostProcess host)
    {
        var deadline = DateTimeOffset.UtcNow + Patience;
        while (!host.Output.Contains("Application started", StringComparison.Ordinal))
        {
            if (DateTimeOffset.UtcNow > deadline)
                throw new TimeoutException($"The host did not report itself started within {Patience}. Host output:{Environment.NewLine}{host.Output}");
            await Task.Delay(100);
        }

        return host.Output;
    }
}
