using System.Net;
using System.Text.Json.Nodes;
using Elsa.Cluster.Core.Options;
using Elsa.Cluster.Hosting;
using Elsa.Primitives.Exceptions;
using static Elsa.Cluster.EntityFrameworkCore.Tests.FeedModuleDatabase;
using static Elsa.Cluster.EntityFrameworkCore.Tests.FoundationHostComposition;

namespace Elsa.Cluster.EntityFrameworkCore.Tests;

/// <summary>
/// #2151's acceptance, on the real thing: two built <c>Elsa.Foundation.Host</c> processes that share one database, each
/// with the EF cluster membership provider enabled from configuration alone, and a feed-loaded EF module whose new schema
/// version one of them runs before the other. They see each other; the new version is finalized only once both can read it;
/// and a feature that needs it stays dormant, with the reason, until then (spec 181, FR-021; spec 182, FR-001 and FR-008;
/// spec 183, FR-023 and FR-024). The engine is the derived class's: SQLite in <see cref="FoundationHostClusterBootTests"/>,
/// PostgreSQL in the container tests of <c>Elsa.Cluster.EntityFrameworkCore.ProviderTests</c>, which link this file.
/// </summary>
/// <remarks>
/// Each host is killed rather than stopped, as a crash is: its entry stays live until it expires, so the upgrade shows the
/// new incarnation of the same host id waiting its predecessor out rather than displacing it.
/// </remarks>
public abstract class FoundationHostClusterScenario(FoundationHostFeed feed, string provider) : IAsyncLifetime
{
    protected const string Older = "foundation-host-a";
    protected const string Newer = "foundation-host-b";

    /// <summary>The upgrade waits out the killed incarnation's expiry, then activates its shell; this covers both.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(90);

    /// <summary>How long a version both hosts cannot read is watched staying unfinalized: many gate evaluations and heartbeats.</summary>
    private static readonly TimeSpan Watch = TimeSpan.FromSeconds(4);

    private readonly List<FoundationHostProcess> _hosts = [];

    /// <summary>Where the hosts' shared database is, in the engine's own connection-string form.</summary>
    protected abstract string ConnectionString { get; }

    /// <summary>Why the engine is not available here, when it is not: the scenario is then skipped rather than failed.</summary>
    protected virtual string? SkipReason => null;

    /// <summary>The fixture module's tables and record in that database.</summary>
    private protected abstract FeedModuleDatabase Database { get; }

    /// <summary>
    /// Every current member of the membership table that has not left, by host id, with the versions of the fixture's family
    /// its latest report says it reads: the fleet a fresh read returns to either host.
    /// </summary>
    protected abstract Task<Dictionary<string, string[]>> ReadableByMemberAsync();

    /// <summary>
    /// What the host acquired from its feeds, checked against everything it carries for its membership: it must hold no
    /// second copy of any of it.
    /// </summary>
    private protected abstract Task AssertBindsTheHostsEfClosureAsync(FoundationHostProcess host);

    protected FoundationHostFeed Feed => feed;

    public virtual Task InitializeAsync() => Task.CompletedTask;

    /// <summary>Stops every host, newest first.</summary>
    public virtual async Task DisposeAsync()
    {
        foreach (var host in Enumerable.Reverse(_hosts))
            await host.DisposeAsync();
    }

    [SkippableFact]
    public async Task Two_hosts_sharing_a_database_finalize_a_feed_loaded_modules_new_version_only_once_both_can_read_it()
    {
        Skip.If(SkipReason is not null, SkipReason);
        await Database.SeedAsync();
        var older = await StartAsync(feed.PreviousDirectory, Clustered(Older), EntityFrameworkCoreFeature);
        var newer = await StartAsync(feed.Directory, Clustered(Newer), EntityFrameworkCoreFeature, OrdersFeature);

        // They see each other: both are current members of the one table every fresh read reads, each reporting what it
        // reads of the family.
        await WaitUntilAsync(async () => Describe(await ReadableByMemberAsync()) == Describe(new() { [Older] = ["1"], [Newer] = ["1", "2"] }));

        // The newer host counts the older one: version 2 is not finalized, and the feature that needs it is dormant and
        // says it waits for every host, rather than that it was held or that nothing was observed.
        var deadline = DateTimeOffset.UtcNow + Watch;
        do
        {
            var (status, text) = await OrdersAsync(newer);
            Assert.Equal((HttpStatusCode.Conflict, "1"), (status, (await Database.RecordAsync()).FinalizedVersion));
            Assert.StartsWith(SchemaDormancyRefusedException.RefusalCode, text, StringComparison.Ordinal);
            Assert.Contains("once every host can read version '2'", text, StringComparison.Ordinal);
            await Task.Delay(TimeSpan.FromMilliseconds(250));
        } while (DateTimeOffset.UtcNow < deadline);

        // The module binds the host's EF closure: neither host acquired EF Core, its engine closure or the EF persistence
        // package from the feed that offers them, only the engine package the ef-provider selection names.
        foreach (var host in new[] { older, newer })
            await AssertBindsTheHostsEfClosureAsync(host);

        // The older host is upgraded: killed, and started again under the same host id on the new release. Its killed
        // incarnation stays live until it expires, so version 2 is still not finalized the moment it is gone.
        await StopAsync(older);
        Assert.Equal("1", (await Database.RecordAsync()).FinalizedVersion);
        var upgraded = await StartAsync(feed.Directory, Clustered(Older), EntityFrameworkCoreFeature, OrdersFeature);

        await WaitUntilAsync(async () => (await Database.RecordAsync()).FinalizedVersion == "2");
        await WaitUntilAsync(async () => (await OrdersAsync(newer)).Status == HttpStatusCode.OK && (await OrdersAsync(upgraded)).Status == HttpStatusCode.OK);
        Assert.Equal(Describe(new() { [Older] = ["1", "2"], [Newer] = ["1", "2"] }), Describe(await ReadableByMemberAsync()));
        await AfterUpgradeAsync(newer, upgraded);
    }

    /// <summary>Runs once the fleet has finalized and both hosts serve, for what only one engine has to show.</summary>
    private protected virtual Task AfterUpgradeAsync(FoundationHostProcess newer, FoundationHostProcess upgraded) => Task.CompletedTask;

    /// <summary>
    /// Starts a host over <paramref name="packages"/> with <paramref name="membership"/> beside the settings every boot test
    /// gives it, enabling <paramref name="features"/>. It is stopped when the test ends, whatever happens.
    /// </summary>
    private protected async Task<FoundationHostProcess> StartAsync(string packages, IEnumerable<KeyValuePair<string, string>> membership, params string[] features)
    {
        var settings = Settings(feed, provider);
        foreach (var (key, value) in membership)
            settings[key] = value;

        var host = await FoundationHostProcess.StartAsync(ShellsOn(provider, ConnectionString, features), packages, settings);
        _hosts.Add(host);
        return host;
    }

    private protected async Task StopAsync(FoundationHostProcess host)
    {
        _hosts.Remove(host);
        await host.DisposeAsync();
    }

    /// <summary>
    /// The EF provider enabled under <paramref name="hostId"/>, over the test's database, with timings short enough that an
    /// upgrade waits seconds for its killed predecessor rather than the default half minute.
    /// </summary>
    protected Dictionary<string, string> Clustered(string hostId) => new()
    {
        [Key(nameof(ClusterMembershipOptions.HostId))] = hostId,
        [Key(nameof(ClusterMembershipOptions.HeartbeatInterval))] = "00:00:00.500",
        [Key(nameof(ClusterMembershipOptions.ExpiryPeriod))] = "00:00:10",
        [Key(nameof(ClusterMembershipOptions.SkewAllowance))] = "00:00:01",
        [Key(EfKey(ClusterMembershipConfigurationExtensions.EnabledKey))] = "true",
        [Key(EfKey(nameof(EfClusterMembershipOptions.Provider)))] = provider,
        [Key(EfKey(nameof(EfClusterMembershipOptions.ConnectionString)))] = ConnectionString,
        [Key(EfKey(nameof(EfClusterMembershipOptions.CleanupPeriod)))] = "00:01:00"
    };

    protected static string Key(string name) => $"{ClusterMembershipOptions.SectionName}:{name}";

    private static string EfKey(string name) => $"{EfClusterMembershipOptions.SectionKey}:{name}";

    protected static string[] ReadableVersions(string reportJson)
    {
        var entries = JsonNode.Parse(reportJson)?["readability"]?["entries"]?.AsArray() ?? [];
        return
        [
            .. entries
                .Where(entry => entry?["family"]?.GetValue<string>() == Family)
                .SelectMany(entry => entry!["readableVersions"]!.AsArray().Select(version => version!.GetValue<string>()))
        ];
    }

    protected static string Describe(Dictionary<string, string[]> readable) =>
        string.Join("; ", readable.OrderBy(member => member.Key, StringComparer.Ordinal).Select(member => $"{member.Key} reads [{string.Join(", ", member.Value)}]"));

    protected Task WaitUntilAsync(Func<Task<bool>> condition) =>
        Polling.UntilAsync(condition, Patience, TimeSpan.FromMilliseconds(200), async () =>
            $"Members: {Describe(await ReadableByMemberAsync())}. " +
            string.Concat(_hosts.Select((host, index) => $"{Environment.NewLine}Host {index} output:{Environment.NewLine}{host.Output}")));
}
